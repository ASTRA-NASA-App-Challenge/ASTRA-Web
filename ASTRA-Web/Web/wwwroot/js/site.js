// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

const spaceCover = document.querySelector("[data-space-cover]");

if (spaceCover) {
    const updateHorizon = (clientX) => {
        const bounds = spaceCover.getBoundingClientRect();
        const position = Math.max(0, Math.min(1, (clientX - bounds.left) / bounds.width));
        spaceCover.style.setProperty("--horizon-position", position.toFixed(3));
    };

    spaceCover.addEventListener("pointermove", (event) => updateHorizon(event.clientX));
    spaceCover.addEventListener("pointerleave", () => {
        spaceCover.style.setProperty("--horizon-position", "0.5");
    });
}
