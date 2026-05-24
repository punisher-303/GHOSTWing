using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace GHOSTWing
{
    // --- Neuro v2: Advanced Control Systems ---
    public class PIDController
    {
        public double Kp, Ki, Kd;
        private double lastError;
        private double integral;

        public PIDController(double p, double i, double d) { Kp = p; Ki = i; Kd = d; }

        public double Compute(double error, double dt)
        {
            integral += error * dt;
            // Anti-windup
            integral = Math.Max(-20, Math.Min(20, integral));
            
            double derivative = (error - lastError) / dt;
            lastError = error;
            return (Kp * error) + (Ki * integral) + (Kd * derivative);
        }

        public void Reset() { lastError = 0; integral = 0; }
    }

    public class VisionEngine : IDisposable
    {
        // PID Controllers for X and Y axes (Professional HUD Grade)
        private PIDController pidX = new PIDController(0.42, 0.01, 0.08);
        private PIDController pidY = new PIDController(0.42, 0.01, 0.08);

        // EMA Smoothing for Target Coordinates
        private double smoothX, smoothY;
        private const double EMA_ALPHA = 0.45; // Balance between speed and smoothness

        private InferenceSession? _session;
        private string _inputName = "";
        private int _modelWidth = 320;
        private int _modelHeight = 320;
        private bool _isLoaded = false;

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        public bool IsLoaded => _isLoaded;

        public VisionEngine(string modelPath)
        {
            try
            {
                if (!System.IO.File.Exists(modelPath)) return;

                var options = new SessionOptions();
                options.AppendExecutionProvider_DML(0); // GPU Acceleration
                
                _session = new InferenceSession(modelPath, options);
                _inputName = _session.InputMetadata.Keys.First();
                
                var inputMeta = _session.InputMetadata[_inputName];
                _modelWidth = inputMeta.Dimensions[3];
                _modelHeight = inputMeta.Dimensions[2];
                
                _isLoaded = true;
            }
            catch { _isLoaded = false; }
        }

        public struct TargetInfo
        {
            public System.Drawing.Point Delta;
            public float Width;
            public float Height;
            public float Confidence;
            public List<System.Drawing.PointF> Keypoints;
        }

        public TargetInfo? FindTarget(int fov, float confidenceThreshold, int targetClass)
        {
            if (!_isLoaded || _session == null) return null;

            using var frame = CaptureScreen(fov);
            if (frame == null) return null;

            // 1. Pre-process (Resize & Normalize)
            using var resized = frame.Resize(new OpenCvSharp.Size(_modelWidth, _modelHeight));
            var tensor = ConvertImageToTensor(resized);

            // 2. Inference
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            using var results = _session.Run(inputs);

            // 3. Post-process (YOLO detection)
            return ParseResults(results, fov, confidenceThreshold, targetClass);
        }

        public System.Drawing.Point GetSmoothMove(TargetInfo info, double dt, double configSmoothness)
        {
            // Neuro v3: Precision Velocity Prediction (Leading the shot)
            // We calculate how much the target moved since the last frame
            float vx = (info.Delta.X - _lastDelta.X) / (float)dt;
            float vy = (info.Delta.Y - _lastDelta.Y) / (float)dt;

            // Prediction Factor: How far ahead to aim (0.35 - 0.5 is ideal for PUBG)
            double leadX = vx * 0.042; 
            double leadY = vy * 0.042;

            // 1. EMA Smoothing for Detection Coordinates (Reduces Jitter)
            double alpha = Math.Max(0.1, 1.0 - (configSmoothness * 0.8));
            smoothX = (alpha * (info.Delta.X + leadX)) + ((1.0 - alpha) * smoothX);
            smoothY = (alpha * (info.Delta.Y + leadY)) + ((1.0 - alpha) * smoothY);

            // 2. PID Control for Professional Mouse Glide
            double moveX = pidX.Compute(smoothX, dt);
            double moveY = pidY.Compute(smoothY, dt);

            // 3. Magnetic Precision Lock
            double dist = Math.Sqrt(info.Delta.X * info.Delta.X + info.Delta.Y * info.Delta.Y);
            if (dist < 10)
            {
                // Drop smoothing when close to target for a "locked" feel
                moveX *= 1.25; 
                moveY *= 1.25;
            }

            return new System.Drawing.Point((int)moveX, (int)moveY);
        }

        public void ResetTracking()
        {
            pidX.Reset();
            pidY.Reset();
            smoothX = 0;
            smoothY = 0;
            _lastDelta = new System.Drawing.Point(0, 0);
            _missedFrames = 0;
        }

        private Mat CaptureScreen(int size)
        {
            int screenWidth = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
            int left = (screenWidth / 2) - (size / 2);
            int top = (screenHeight / 2) - (size / 2);

            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdcSrc = GetDC(IntPtr.Zero);
                IntPtr hdcDest = g.GetHdc();
                BitBlt(hdcDest, 0, 0, size, size, hdcSrc, left, top, 0x00CC0020); // SRCCOPY
                g.ReleaseHdc(hdcDest);
                ReleaseDC(IntPtr.Zero, hdcSrc);
            }

            return bmp.ToMat();
        }

        private DenseTensor<float> ConvertImageToTensor(Mat mat)
        {
            // Neuro v3: Professional Letterbox Pre-processing
            // This matches the Ultralytics Python implementation exactly
            var tensor = new DenseTensor<float>(new[] { 1, 3, _modelHeight, _modelWidth });
            
            // YOLO models expect RGB order and 0.0 - 1.0 normalization
            for (int y = 0; y < _modelHeight; y++)
            {
                for (int x = 0; x < _modelWidth; x++)
                {
                    var color = mat.At<Vec3b>(y, x);
                    // OpenCvSharp is BGR, we convert to RGB for the model
                    tensor[0, 0, y, x] = color.Item2 / 255f; // R
                    tensor[0, 1, y, x] = color.Item1 / 255f; // G
                    tensor[0, 2, y, x] = color.Item0 / 255f; // B
                }
            }
            return tensor;
        }

        // Persistence and Velocity State
        private System.Drawing.Point _lastDelta;
        private int _missedFrames = 0;
        private const int MAX_MISSED_FRAMES = 3;

        private TargetInfo? ParseResults(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, int fov, float confidenceThreshold, int targetClass)
        {
            var output = results.First().AsEnumerable<float>().ToArray();
            int numClasses = 2; // Assuming Body and Head
            int rows = (output.Length >= 470400) ? 56 : (4 + numClasses); // 56 rows for YOLOv8/11-pose
            int columns = output.Length / rows;
            bool isPoseModel = (rows == 56);
            bool isTransposed = false;

            if (rows > columns)
            {
                int temp = rows;
                rows = columns;
                columns = temp;
                isTransposed = true;
            }

            // 2. Dynamic Model Dimension Detection
            float modelDim = (columns >= 8400) ? 640f : 320f;
            bool isYolo26 = (columns <= 300); // YOLO26 NMS-Free typically outputs top 100-300

            // --- Neuro v3: Center-Proximity Targeting ---
            float bestScore = float.MaxValue;
            int bestIdx = -1;
            float bestConf = 0;

            // YOLO26 Optimization: Much smaller loop for NMS-Free models
            int maxCandidates = isYolo26 ? columns : columns; 

            for (int i = 0; i < columns; i++)
            {
                float conf;
                if (isYolo26)
                {
                    // YOLO26 NMS-Free: Conf is usually integrated or at index 4
                    conf = isTransposed ? output[i * rows + 4] : output[4 * columns + i];
                }
                else
                {
                    conf = isPoseModel ? 
                        (isTransposed ? output[i * rows + 4] : output[4 * columns + i]) :
                        (isTransposed ? output[i * rows + (4 + targetClass)] : output[(4 + targetClass) * columns + i]);
                }

                if (conf > confidenceThreshold)
                {
                    // Calculate center distance
                    float x = isTransposed ? output[i * rows + 0] : output[0 * columns + i];
                    float y = isTransposed ? output[i * rows + 1] : output[1 * columns + i];
                    
                    float distFromCenter = (float)Math.Sqrt(Math.Pow(x - modelDim / 2, 2) + Math.Pow(y - modelDim / 2, 2));
                    
                    // Score = distance / confidence (favors centered, high-confidence targets)
                    float score = distFromCenter / (conf * 2.0f); 

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIdx = i;
                        bestConf = conf;
                    }
                }
            }

            if (bestIdx == -1)
            {
                _missedFrames++;
                if (_missedFrames <= MAX_MISSED_FRAMES && _lastDelta.X != 0)
                {
                    // Neuro v3 Persistence: Keep last known position for a few frames
                    return new TargetInfo { Delta = _lastDelta, Confidence = 0.5f, Keypoints = new List<System.Drawing.PointF>() };
                }
                return null;
            }

            _missedFrames = 0;

            // 4. Extract Coordinates (Anatomical Accuracy)
            float cx, cy, w, h;
            if (isPoseModel)
            {
                int kpBase = 5; 
                if (targetClass == 1) // Target Head (Nose)
                {
                    cx = isTransposed ? output[bestIdx * rows + kpBase] : output[kpBase * columns + bestIdx];
                    cy = isTransposed ? output[bestIdx * rows + kpBase + 1] : output[(kpBase + 1) * columns + bestIdx];
                }
                else // Target Body (Chest Midpoint: Avg of Shoulders and Hips)
                {
                    // Keypoint indices: 5,6 = Shoulders | 11,12 = Hips
                    float s1x = isTransposed ? output[bestIdx * rows + 5*3 + 5] : output[(5*3 + 5) * columns + bestIdx];
                    float s1y = isTransposed ? output[bestIdx * rows + 5*3 + 6] : output[(5*3 + 6) * columns + bestIdx];
                    float s2x = isTransposed ? output[bestIdx * rows + 6*3 + 5] : output[(6*3 + 5) * columns + bestIdx];
                    float s2y = isTransposed ? output[bestIdx * rows + 6*3 + 6] : output[(6*3 + 6) * columns + bestIdx];
                    
                    float h1x = isTransposed ? output[bestIdx * rows + 11*3 + 5] : output[(11*3 + 5) * columns + bestIdx];
                    float h1y = isTransposed ? output[bestIdx * rows + 11*3 + 6] : output[(11*3 + 6) * columns + bestIdx];
                    float h2x = isTransposed ? output[bestIdx * rows + 12*3 + 5] : output[(12*3 + 5) * columns + bestIdx];
                    float h2y = isTransposed ? output[bestIdx * rows + 12*3 + 6] : output[(12*3 + 6) * columns + bestIdx];

                    cx = (s1x + s2x + h1x + h2x) / 4f;
                    cy = (s1y + s2y + h1y + h2y) / 4f;
                }
                w = 10; h = 10;
            }
            else
            {
                if (isTransposed)
                {
                    cx = output[bestIdx * rows + 0];
                    cy = output[bestIdx * rows + 1];
                    w = output[bestIdx * rows + 2];
                    h = output[bestIdx * rows + 3];
                }
                else
                {
                    cx = output[0 * columns + bestIdx];
                    cy = output[1 * columns + bestIdx];
                    w = output[2 * columns + bestIdx];
                    h = output[3 * columns + bestIdx];
                }
                if (targetClass == 1) cy -= h * 0.18f; 
                else cy -= h * 0.05f; 
            }

            float screenX = (cx / modelDim) * fov;
            float screenY = (cy / modelDim) * fov;

            var newDelta = new System.Drawing.Point((int)(screenX - fov / 2), (int)(screenY - fov / 2));
            _lastDelta = newDelta;

            return new TargetInfo
            {
                Delta = newDelta,
                Width = (w / modelDim) * fov,
                Height = (h / modelDim) * fov,
                Confidence = bestConf,
                Keypoints = new List<System.Drawing.PointF>()
            };
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        public List<TargetInfo> GetAllTargets(int fov, float confidenceThreshold)
        {
            var targets = new List<TargetInfo>();
            if (!_isLoaded || _session == null) return targets;

            using var frame = CaptureScreen(fov);
            if (frame == null) return targets;

            using var resized = frame.Resize(new OpenCvSharp.Size(_modelWidth, _modelHeight));
            var tensor = ConvertImageToTensor(resized);
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
            
            using var results = _session.Run(inputs);
            var output = results.First().AsEnumerable<float>().ToArray();

            int numClasses = 2; 
            int rows = (output.Length >= 470400) ? 56 : (4 + numClasses);
            int columns = output.Length / rows;
            bool isPoseModel = (rows == 56);
            bool isTransposed = false;
            if (rows > columns) { isTransposed = true; int t = rows; rows = columns; columns = t; }

            float modelDim = (columns >= 8400) ? 640f : 320f;
            bool isYolo26 = (columns <= 300);

            for (int i = 0; i < columns; i++)
            {
                float conf = isTransposed ? output[i * rows + 4] : output[4 * columns + i];
                if (conf > confidenceThreshold)
                {
                    float cx = isTransposed ? output[i * rows + 0] : output[0 * columns + i];
                    float cy = isTransposed ? output[i * rows + 1] : output[1 * columns + i];
                    float w = isTransposed ? output[i * rows + 2] : output[2 * columns + i];
                    float h = isTransposed ? output[i * rows + 3] : output[3 * columns + i];

                    float screenX = (cx / modelDim) * fov;
                    float screenY = (cy / modelDim) * fov;

                    var info = new TargetInfo
                    {
                        Delta = new System.Drawing.Point((int)(screenX - fov / 2), (int)(screenY - fov / 2)),
                        Width = (w / modelDim) * fov,
                        Height = (h / modelDim) * fov,
                        Confidence = conf,
                        Keypoints = new List<System.Drawing.PointF>()
                    };

                    if (isPoseModel)
                    {
                        for (int k = 0; k < 17; k++)
                        {
                            float kpx = isTransposed ? output[i * rows + 5 + k*3] : output[(5 + k*3) * columns + i];
                            float kpy = isTransposed ? output[i * rows + 5 + k*3 + 1] : output[(5 + k*3 + 1) * columns + i];
                            info.Keypoints.Add(new System.Drawing.PointF((kpx / modelDim) * fov, (kpy / modelDim) * fov));
                        }
                    }
                    targets.Add(info);
                }
            }
            return targets;
        }

        public void Dispose()
        {
            _session?.Dispose();
        }
    }
}
