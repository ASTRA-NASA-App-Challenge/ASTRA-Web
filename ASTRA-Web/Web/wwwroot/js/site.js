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

document.querySelectorAll("[data-chart-tooltip]").forEach((bar) => {
    bar.addEventListener("mouseenter", () => bar.classList.add("is-tooltip-visible"));
    bar.addEventListener("mouseleave", () => bar.classList.remove("is-tooltip-visible"));
    bar.addEventListener("focus", () => bar.classList.add("is-tooltip-visible"));
    bar.addEventListener("blur", () => bar.classList.remove("is-tooltip-visible"));
});

const crewRoster = document.querySelector("[data-crew-roster]");
if (crewRoster) {
    const cards = [...crewRoster.querySelectorAll("[data-crew-member]")];
    const search = crewRoster.querySelector("[data-crew-search]");
    const sort = crewRoster.querySelector("[data-crew-sort]");
    const filters = [...crewRoster.querySelectorAll("[data-crew-filter]")];
    const grid = crewRoster.querySelector("[data-crew-grid]");
    const empty = crewRoster.querySelector("[data-crew-empty]");
    const reset = crewRoster.querySelector("[data-crew-reset]");
    const results = crewRoster.querySelector("[data-crew-results]");
    let role = "all";
    let attentionOnly = false;
    const attention = crewRoster.querySelector('[data-crew-attention]');
    attention?.addEventListener('click', () => {
        attentionOnly = !attentionOnly;
        attention.setAttribute('aria-pressed', String(attentionOnly));
        update();
    });
    const normalize = (value) => value.normalize("NFKD").replace(/\p{Diacritic}/gu, "").toLocaleLowerCase();
    const sortMembers = () => {
        const ordered = [...cards].sort((a, b) => {
            const nameOrder = a.dataset.name.localeCompare(b.dataset.name, undefined, { sensitivity: "base", numeric: true });
            if (sort.value === "attention") return Number(b.dataset.priority) - Number(a.dataset.priority) || nameOrder;
            if (sort.value === "role") return a.dataset.role.localeCompare(b.dataset.role) || nameOrder;
            return sort.value === "desc" ? -nameOrder : nameOrder;
        });
        const fragment = document.createDocumentFragment();
        ordered.forEach((card) => fragment.append(card));
        grid.append(fragment);
    };
    const update = () => {
        const query = normalize(search.value.trim());
        let visible = 0;
        cards.forEach((card) => {
            card.hidden = !(role === "all" || card.dataset.role === role)
                || !normalize(`${card.dataset.name} ${card.dataset.username}`).includes(query)
                || (attentionOnly && card.dataset.attention !== "true");
            if (!card.hidden) visible++;
        });
        empty.hidden = visible > 0;
        reset.hidden = !query && role === "all" && !attentionOnly;
        results.textContent = `${visible} of ${cards.length} ${cards.length === 1 ? "member" : "members"}`;
        filters.forEach((button) => button.setAttribute("aria-pressed", String(button.dataset.crewFilter === role)));
    };
    search.addEventListener("input", update);
    sort.addEventListener("change", () => { sortMembers(); update(); });
    filters.forEach((button) => button.addEventListener("click", () => { role = button.dataset.crewFilter; update(); }));
    reset.addEventListener("click", () => { search.value = ""; role = "all"; attentionOnly = false; attention?.setAttribute("aria-pressed", "false"); update(); search.focus(); });
    crewRoster.querySelectorAll("[data-crew-reveal]").forEach((button) => {
        button.hidden = false;
        button.addEventListener("click", () => {
            const input = document.getElementById(button.getAttribute("aria-controls"));
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            button.setAttribute("aria-pressed", String(show));
            button.setAttribute("aria-label", `${show ? "Hide" : "Show"} PIN for ${button.dataset.memberName}`);
        });
    });
    crewRoster.querySelectorAll("[data-crew-copy]").forEach((button) => {
        const feedback = button.closest("[data-crew-member]").querySelector("[data-crew-feedback]");
        let feedbackTimer;
        button.hidden = false;
        button.addEventListener("click", async () => {
            clearTimeout(feedbackTimer);
            try {
                await navigator.clipboard.writeText(document.getElementById(button.getAttribute("aria-controls")).value);
                feedback.textContent = "PIN copied to clipboard.";
            } catch {
                feedback.textContent = "Copy is unavailable. Reveal the PIN to read it.";
            }
            feedback.classList.add("is-visible");
            feedbackTimer = setTimeout(() => { feedback.classList.remove("is-visible"); }, 3500);
        });
    });
    sortMembers();
    update();
}

const addMemberModal = document.getElementById("add-member-modal");
if (addMemberModal) {
    const form = addMemberModal.querySelector("[data-add-member-form]");
    const submit = form.querySelector("[data-add-member-submit]");
    addMemberModal.addEventListener("shown.bs.modal", () => {
        (form.querySelector(".input-validation-error") || form.querySelector("input:not([type='hidden'])"))?.focus();
    });
    form.addEventListener("invalid", (event) => event.target.classList.add("input-validation-error"), true);
    form.addEventListener("input", (event) => {
        if (event.target.validity?.valid) event.target.classList.remove("input-validation-error");
    });
    form.addEventListener("submit", () => {
        if (!form.checkValidity()) return;
        submit.disabled = true;
        submit.textContent = "Adding member…";
        form.setAttribute("aria-busy", "true");
    });
    window.addEventListener("pageshow", () => {
        submit.disabled = false;
        submit.textContent = "Add member";
        form.removeAttribute("aria-busy");
    });
    if (addMemberModal.dataset.openOnLoad === "true") {
        bootstrap.Modal.getOrCreateInstance(addMemberModal).show();
    }
}

const editMemberModal = document.getElementById("edit-member-modal");
if (editMemberModal) {
    const form = editMemberModal.querySelector("[data-edit-member-form]");
    const submit = form.querySelector("[data-member-save]");
    editMemberModal.addEventListener("show.bs.modal", (event) => {
        if (!event.relatedTarget) return;
        const member = event.relatedTarget.dataset;
        const values = { Id: member.memberId, FirstName: member.firstName, MiddleName: member.middleName,
            LastName: member.lastName, Username: member.username, Role: member.role };
        Object.entries(values).forEach(([key, value]) => { form.elements[`EditedMember.${key}`].value = value || ""; });
        form.querySelectorAll(".input-validation-error").forEach((field) => field.classList.remove("input-validation-error"));
        form.querySelectorAll(".field-validation-error, .astra-modal-errors").forEach((error) => { error.textContent = ""; });
    });
    editMemberModal.addEventListener("shown.bs.modal", () => {
        (form.querySelector(".input-validation-error") || form.elements["EditedMember.FirstName"])?.focus();
    });
    form.addEventListener("invalid", (event) => event.target.classList.add("input-validation-error"), true);
    form.addEventListener("input", (event) => {
        if (event.target.validity?.valid) event.target.classList.remove("input-validation-error");
    });
    form.addEventListener("submit", () => {
        if (!form.checkValidity()) return;
        submit.disabled = true;
        submit.textContent = "Saving changes…";
        form.setAttribute("aria-busy", "true");
    });
    window.addEventListener("pageshow", () => {
        submit.disabled = false;
        submit.textContent = "Save changes";
        form.removeAttribute("aria-busy");
    });
    if (editMemberModal.dataset.openOnLoad === "true") bootstrap.Modal.getOrCreateInstance(editMemberModal).show();
}

const deleteMemberModal = document.getElementById("delete-member-modal");
if (deleteMemberModal) {
    const form = deleteMemberModal.querySelector("[data-delete-member-form]");
    const submit = form.querySelector("[data-member-delete-submit]");
    deleteMemberModal.addEventListener("show.bs.modal", (event) => {
        if (!event.relatedTarget) return;
        const member = event.relatedTarget.dataset;
        form.elements["DeletedMember.Id"].value = member.memberId;
        deleteMemberModal.querySelector("[data-delete-member-name]").textContent = member.memberName;
        form.querySelectorAll(".astra-modal-errors").forEach((error) => { error.textContent = ""; });
    });
    deleteMemberModal.addEventListener("shown.bs.modal", () => deleteMemberModal.querySelector("[data-delete-cancel]").focus());
    form.addEventListener("submit", () => {
        submit.disabled = true;
        submit.textContent = "Deleting…";
        form.setAttribute("aria-busy", "true");
    });
    window.addEventListener("pageshow", () => {
        submit.disabled = false;
        submit.textContent = "Delete member";
        form.removeAttribute("aria-busy");
    });
    if (deleteMemberModal.dataset.openOnLoad === "true") bootstrap.Modal.getOrCreateInstance(deleteMemberModal).show();
}
