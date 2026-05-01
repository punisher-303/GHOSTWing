// Smooth Scroll
document.querySelectorAll('a[href^="#"]').forEach(anchor => {
    anchor.addEventListener('click', function (e) {
        e.preventDefault();
        const target = document.querySelector(this.getAttribute('href'));
        if (target) {
            target.scrollIntoView({ behavior: 'smooth' });
        }
    });
});

// Dynamic GitHub Release Fetching
async function updateDownloadButton() {
    const btn = document.getElementById('download-btn');
    if (!btn) return;

    try {
        const response = await fetch('https://api.github.com/repos/punisher-303/GHOSTWing/releases/latest');
        const data = await response.json();

        if (data.tag_name) {
            btn.innerText = `Download ${data.tag_name}`;
            btn.href = data.html_url;
        } else {
            // Fallback if no release found
            btn.href = "https://github.com/punisher-303/GHOSTWing/releases";
        }
    } catch (error) {
        console.error("Error fetching release:", error);
        btn.href = "https://github.com/punisher-303/GHOSTWing/releases";
    }
}

updateDownloadButton();

// Reveal animations on scroll
const observerOptions = { threshold: 0.1 };
const observer = new IntersectionObserver((entries) => {
    entries.forEach(entry => {
        if (entry.isIntersecting) {
            entry.target.style.opacity = "1";
            entry.target.style.transform = "translateY(0)";
        }
    });
}, observerOptions);

document.querySelectorAll('.glass, .plan-card').forEach(el => {
    el.style.opacity = "0";
    el.style.transform = "translateY(20px)";
    el.style.transition = "0.6s ease-out";
    observer.observe(el);
});
