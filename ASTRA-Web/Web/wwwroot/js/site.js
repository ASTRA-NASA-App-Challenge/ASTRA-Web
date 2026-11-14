// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const spaceCover = document.querySelector("[data-space-cover]");

if (spaceCover) {
    const updateHorizon = (clientX) => {
        const bounds = spaceCover.getBoundingClientRect();
        const position = Math.max(0, Math.min(1, (clientX - bounds.left) / bounds.width));
        spaceCover.style.setProperty("--horizon-position", position.toFixed(3));
    };

    spaceCover.addEventListener("pointermove", (event) => {
        updateHorizon(event.clientX);
    });
    spaceCover.addEventListener("pointerleave", () => {
        spaceCover.style.setProperty("--horizon-position", "0.5");
    });
}

document.querySelectorAll("[data-secret-toggle]").forEach((button) => {
    const input = document.getElementById(button.getAttribute("aria-controls"));
    if (!input) return;
    button.hidden = false;
    button.addEventListener("click", () => {
        const reveal = input.type === "password";
        input.type = reveal ? "text" : "password";
        button.setAttribute("aria-label", reveal ? "Hide password or PIN" : "Show password or PIN");
        button.setAttribute("aria-pressed", String(reveal));
    });
});

const loginForm = document.querySelector("[data-login-form]");
if (loginForm) {
    const submit = loginForm.querySelector("[type='submit']");
    const label = loginForm.querySelector("[data-login-label]");
    loginForm.addEventListener("submit", () => {
        if (!loginForm.checkValidity()) return;
        submit.disabled = true;
        loginForm.setAttribute("aria-busy", "true");
        label.textContent = "Login in progress…";
    });
    window.addEventListener("pageshow", () => {
        submit.disabled = false;
        loginForm.removeAttribute("aria-busy");
        label.textContent = "Login";
    });
}
