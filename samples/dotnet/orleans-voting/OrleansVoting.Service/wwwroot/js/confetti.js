// Grain Poll — self-contained celebration confetti.
// Progressive enhancement: invoked from the voting flow via JS interop.
// No-ops when the user prefers reduced motion. No external dependencies.
(function () {
    "use strict";

    var COLORS = ["#ff49a3", "#2de2e2", "#ffcb45", "#b072ff", "#34e0a1", "#ffffff"];
    var canvas = null;
    var ctx = null;
    var particles = [];
    var running = false;

    function prefersReducedMotion() {
        return window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    }

    function ensureCanvas() {
        if (canvas) {
            return;
        }
        canvas = document.createElement("canvas");
        canvas.setAttribute("aria-hidden", "true");
        var s = canvas.style;
        s.position = "fixed";
        s.left = "0";
        s.top = "0";
        s.width = "100%";
        s.height = "100%";
        s.pointerEvents = "none";
        s.zIndex = "2000";
        document.body.appendChild(canvas);
        ctx = canvas.getContext("2d");
        resize();
        window.addEventListener("resize", resize);
    }

    function resize() {
        if (!canvas) {
            return;
        }
        var dpr = window.devicePixelRatio || 1;
        canvas.width = Math.floor(window.innerWidth * dpr);
        canvas.height = Math.floor(window.innerHeight * dpr);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }

    function spawn(count) {
        var w = window.innerWidth;
        for (var i = 0; i < count; i++) {
            particles.push({
                x: w * (0.2 + Math.random() * 0.6),
                y: -20 - Math.random() * window.innerHeight * 0.3,
                vx: (Math.random() - 0.5) * 6,
                vy: 3 + Math.random() * 5,
                size: 6 + Math.random() * 7,
                rot: Math.random() * Math.PI,
                vr: (Math.random() - 0.5) * 0.3,
                color: COLORS[(Math.random() * COLORS.length) | 0],
                life: 1
            });
        }
    }

    function frame() {
        if (!running) {
            return;
        }
        ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
        var alive = 0;
        for (var i = 0; i < particles.length; i++) {
            var p = particles[i];
            if (p.life <= 0) {
                continue;
            }
            p.vy += 0.12;
            p.x += p.vx;
            p.y += p.vy;
            p.rot += p.vr;
            if (p.y > window.innerHeight + 40) {
                p.life = 0;
                continue;
            }
            alive++;
            ctx.save();
            ctx.translate(p.x, p.y);
            ctx.rotate(p.rot);
            ctx.fillStyle = p.color;
            ctx.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.62);
            ctx.restore();
        }
        if (alive === 0) {
            stop();
            return;
        }
        requestAnimationFrame(frame);
    }

    function stop() {
        running = false;
        particles = [];
        if (ctx) {
            ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
        }
    }

    function celebrate() {
        if (prefersReducedMotion()) {
            return;
        }
        ensureCanvas();
        spawn(140);
        if (!running) {
            running = true;
            requestAnimationFrame(frame);
        }
    }

    window.grainPoll = window.grainPoll || {};
    window.grainPoll.celebrate = celebrate;
})();
