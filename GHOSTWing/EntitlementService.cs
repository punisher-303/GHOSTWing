using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

namespace GHOSTWing
{
    public class UserEntitlements
    {
        public string Uuid { get; set; } = "";
        public string UserName { get; set; } = "GHOST GUEST";
        public bool IsVip { get; set; } = false;
        public int PlanId { get; set; } = 1; // 1=FREE, 2=GOLD, 3=DIAMOND
        public DateTime PurchaseDate { get; set; } = DateTime.Now;
        public DateTime? ExpiryDate { get; set; } = null; // NULL = Lifetime
        
        public Dictionary<string, bool> Tabs { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, bool> Features { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, bool> Maintenance { get; set; } = new Dictionary<string, bool>();
    }

    public class PlanFeature
    {
        public string Name { get; set; } = "";
        public bool Value { get; set; } = false;
    }

    public class AppPlan
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Price { get; set; } = "";
        public string Description { get; set; } = "";
        public string CheckoutUrl { get; set; } = "";
        public string ColorHex { get; set; } = "#FFFFFF";
        public List<PlanFeature> Features { get; set; } = new List<PlanFeature>();
    }

    public class EntitlementService
    {
        private static readonly HttpClient client = new HttpClient();
        
        // Secured via git-ignored Secrets class
        private static string SupabaseUrl => Secrets.SupabaseUrl;
        private static string SupabaseKey => Secrets.SupabaseKey;

        private readonly Dictionary<string, bool> MasterTabs = new Dictionary<string, bool>
        {
            { "Recoil", true }, { "Crosshair", true }, { "Intelligence", false }, { "Settings", true }, { "Account", true }
        };

        private readonly Dictionary<string, bool> MasterFeatures = new Dictionary<string, bool>
        {
            { "EngineStatus", true },
            { "StreamerMode", true },
            { "JitterEngine", true }, 
            { "TacticalPeek", true },
            { "AutoPause", false },
            { "CrosshairActive", true },
            { "StartWithWindows", true },
            { "MinimizeToTray", true },
            { "StartMinimized", true }
        };

        public async Task<UserEntitlements> FetchEntitlements(string uuid)
        {
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("apikey", SupabaseKey);
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + SupabaseKey);

                var userResponse = await client.GetAsync($"{SupabaseUrl}/rest/v1/user_access?uuid=eq.{uuid}&select=*");
                if (!userResponse.IsSuccessStatusCode) return GetDefaultTrial(uuid);

                var userJson = await userResponse.Content.ReadAsStringAsync();
                var users = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(userJson);

                if (users == null || users.Count == 0)
                {
                    await RegisterNewUser(uuid);
                    return await FetchEntitlements(uuid);
                }

                var userData = users[0];
                var ent = MapBaseInfo(userData);
                bool isVip = ent.IsVip;
                bool useCustomAccess = userData.TryGetValue("access", out var acc) && acc?.ToString()?.ToLower() == "true";

                await SyncMissingKeys("user_access", userData, $"uuid=eq.{uuid}");

                if (useCustomAccess)
                {
                    ApplyPermissions(ent, userData);
                }
                else
                {
                    await FetchAndSyncGlobalTable("free_config");
                    await FetchAndSyncGlobalTable("vip_config");

                    string table = isVip ? "vip_config" : "free_config";
                    var configResponse = await client.GetAsync($"{SupabaseUrl}/rest/v1/{table}?id=eq.1&select=*");
                    
                    if (configResponse.IsSuccessStatusCode)
                    {
                        var configJson = await configResponse.Content.ReadAsStringAsync();
                        var configs = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(configJson);
                        if (configs != null && configs.Count > 0)
                        {
                            var configData = configs[0];
                            if (isVip && configData.TryGetValue("is_enabled", out var enabled) && enabled?.ToString()?.ToLower() == "false")
                            {
                                isVip = false;
                                ent.IsVip = false;
                                var freeRes = await client.GetAsync($"{SupabaseUrl}/rest/v1/free_config?id=eq.1&select=*");
                                if (freeRes.IsSuccessStatusCode)
                                {
                                    var fJson = await freeRes.Content.ReadAsStringAsync();
                                    var fConfigs = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(fJson);
                                    if (fConfigs != null && fConfigs.Count > 0) configData = fConfigs[0];
                                }
                            }
                            ApplyPermissions(ent, configData);
                        }
                    }
                }

                return ent;
            }
            catch { return GetDefaultTrial(uuid); }
        }

        public async Task<List<AppPlan>> FetchPlans()
        {
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("apikey", SupabaseKey);
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + SupabaseKey);

                var response = await client.GetAsync($"{SupabaseUrl}/rest/v1/plans_config?select=*&order=id.asc");
                if (!response.IsSuccessStatusCode) return new List<AppPlan>();

                var json = await response.Content.ReadAsStringAsync();
                var rawPlans = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                var plans = new List<AppPlan>();

                if (rawPlans != null)
                {
                    foreach (var raw in rawPlans)
                    {
                        var p = new AppPlan
                        {
                            Id = int.Parse(raw["id"]?.ToString() ?? "0"),
                            Name = raw["name"]?.ToString() ?? "",
                            Price = raw["price"]?.ToString() ?? "",
                            Description = raw["description"]?.ToString() ?? "",
                            CheckoutUrl = raw["checkout_url"]?.ToString() ?? "",
                            ColorHex = raw["color_hex"]?.ToString() ?? "#FFFFFF",
                            Features = ParseOrderedFeatures(raw, "features")
                        };
                        plans.Add(p);
                    }
                }
                return plans;
            }
            catch { return new List<AppPlan>(); }
        }

        private List<PlanFeature> ParseOrderedFeatures(Dictionary<string, object> data, string key)
        {
            var list = new List<PlanFeature>();
            if (data.TryGetValue(key, out var obj) && obj is JsonElement json)
            {
                if (json.ValueKind == JsonValueKind.Array)
                {
                    // Handle Ordered List: [{"name":"A", "value":true}, ...]
                    foreach (var item in json.EnumerateArray())
                    {
                        list.Add(new PlanFeature { 
                            Name = item.GetProperty("name").GetString() ?? "", 
                            Value = item.GetProperty("value").GetBoolean() 
                        });
                    }
                }
                else if (json.ValueKind == JsonValueKind.Object)
                {
                    // Fallback to old Unordered Object format
                    foreach (var prop in json.EnumerateObject())
                    {
                        list.Add(new PlanFeature { Name = prop.Name, Value = prop.Value.GetBoolean() });
                    }
                }
            }
            return list;
        }

        private async Task FetchAndSyncGlobalTable(string tableName)
        {
            try
            {
                var response = await client.GetAsync($"{SupabaseUrl}/rest/v1/{tableName}?id=eq.1&select=*");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var configs = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                    if (configs != null && configs.Count > 0)
                    {
                        await SyncMissingKeys(tableName, configs[0], "id=eq.1");
                    }
                }
            }
            catch { }
        }

        private async Task SyncMissingKeys(string table, Dictionary<string, object> currentData, string filter)
        {
            try
            {
                var dbTabs = ParseJsonB(currentData, "tabs");
                var dbFeatures = ParseJsonB(currentData, "features");
                bool needsUpdate = false;

                foreach (var master in MasterTabs)
                {
                    if (!dbTabs.ContainsKey(master.Key)) { dbTabs[master.Key] = master.Value; needsUpdate = true; }
                }
                foreach (var master in MasterFeatures)
                {
                    if (!dbFeatures.ContainsKey(master.Key)) { dbFeatures[master.Key] = master.Value; needsUpdate = true; }
                }

                if (needsUpdate)
                {
                    object update;
                    if (table == "user_access")
                        update = new { tabs = dbTabs, features = dbFeatures, last_seen = DateTime.Now };
                    else
                        update = new { tabs = dbTabs, features = dbFeatures };

                    await client.PatchAsync($"{SupabaseUrl}/rest/v1/{table}?{filter}", 
                        new StringContent(JsonSerializer.Serialize(update), Encoding.UTF8, "application/json"));
                }
                else if (table == "user_access")
                {
                    var lastSeen = new { last_seen = DateTime.Now };
                    await client.PatchAsync($"{SupabaseUrl}/rest/v1/user_access?{filter}", 
                        new StringContent(JsonSerializer.Serialize(lastSeen), Encoding.UTF8, "application/json"));
                }
            }
            catch { }
        }

        private void ApplyPermissions(UserEntitlements ent, Dictionary<string, object> data)
        {
            var tabs = ParseJsonB(data, "tabs");
            foreach (var t in tabs) ent.Tabs[t.Key] = t.Value;

            var feats = ParseJsonB(data, "features");
            foreach (var f in feats) ent.Features[f.Key] = f.Value;

            var maint = ParseJsonB(data, "maintenance");
            foreach (var m in maint) ent.Maintenance[m.Key] = m.Value;
        }

        private Dictionary<string, bool> ParseJsonB(Dictionary<string, object> data, string key)
        {
            var dict = new Dictionary<string, bool>();
            if (data.TryGetValue(key, out var obj) && obj != null)
            {
                if (obj is JsonElement json)
                {
                    if (json.ValueKind == JsonValueKind.Object)
                        foreach (var prop in json.EnumerateObject()) dict[prop.Name] = prop.Value.GetBoolean();
                }
                else if (obj is IDictionary<string, object> d)
                {
                    foreach (var kvp in d) if (kvp.Value is bool b) dict[kvp.Key] = b;
                }
            }
            return dict;
        }

        private UserEntitlements MapBaseInfo(Dictionary<string, object> data)
        {
            var ent = new UserEntitlements { Uuid = data["uuid"]?.ToString() ?? "" };
            if (data.TryGetValue("username", out var name)) ent.UserName = name?.ToString() ?? "USER";
            if (data.TryGetValue("plan_id", out var pId)) ent.PlanId = int.TryParse(pId?.ToString(), out int id) ? id : 1;
            if (data.TryGetValue("is_vip", out var vip)) ent.IsVip = vip?.ToString()?.ToLower() == "true";
            if (data.TryGetValue("purchase_date", out var pDate) && DateTime.TryParse(pDate?.ToString(), out var pVal)) ent.PurchaseDate = pVal;
            
            if (data.TryGetValue("expiry_date", out var eDate) && eDate != null && !string.IsNullOrEmpty(eDate.ToString()))
            {
                if (DateTime.TryParse(eDate.ToString(), out var eVal)) ent.ExpiryDate = eVal;
            }
            else
            {
                ent.ExpiryDate = null; // Explicitly lifetime
            }
            return ent;
        }

        private async Task RegisterNewUser(string uuid)
        {
            var newUser = new
            {
                uuid = uuid,
                username = "GHOST GUEST",
                is_vip = false,
                purchase_date = DateTime.Now,
                expiry_date = DateTime.Now.AddDays(1),
                tabs = MasterTabs,
                features = MasterFeatures,
                access = false,
                last_seen = DateTime.Now
            };
            await client.PostAsync($"{SupabaseUrl}/rest/v1/user_access", 
                new StringContent(JsonSerializer.Serialize(newUser), Encoding.UTF8, "application/json"));
        }

        private UserEntitlements GetDefaultTrial(string uuid)
        {
            return new UserEntitlements { Uuid = uuid, UserName = "OFFLINE", Tabs = MasterTabs, Features = MasterFeatures };
        }
    }
}
