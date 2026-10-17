
(function () {
    "use strict";

    function isVisibleAndEditable(el) {
        if (!el || el.disabled || el.readOnly) return false;
        if (el.offsetParent === null) return false;
        const style = window.getComputedStyle(el);
        return style.display !== "none" && style.visibility !== "hidden";
    }

    function getNavigableFields(container) {
        const selector = [
            'input:not([type="hidden"]):not([type="submit"]):not([type="button"]):not([type="reset"]):not([type="checkbox"]):not([type="radio"])',
            "select",
            "textarea"
        ].join(", ");

        return Array.from(container.querySelectorAll(selector)).filter(isVisibleAndEditable);
    }

    function focusElement(el) {
        if (!el) return;
        el.focus();
        if (typeof el.select === "function" && el.tagName === "INPUT") {
            try {
                el.select();
            } catch (_) { }
        }
    }

    // Intercept Enter and ArrowUp/ArrowDown in capture phase before any form submit triggers
    document.addEventListener("keydown", function (e) {
        const target = e.target;
        if (!target || !target.tagName) return;

        const tag = target.tagName.toUpperCase();
        const type = (target.getAttribute("type") || "").toLowerCase();

        // Allow buttons and links to activate normally on Enter
        if (tag === "BUTTON" || tag === "A" || type === "submit" || type === "button" || type === "reset") {
            return;
        }

        const isInputOrSelect = (tag === "INPUT" && type !== "checkbox" && type !== "radio") || tag === "SELECT";
        const isTextarea = tag === "TEXTAREA";

        if (!isInputOrSelect && !isTextarea) {
            return;
        }

        // ArrowDown / ArrowUp inside table inputs (Cutting, Quality, Delivery, Fabrics)
        if ((e.key === "ArrowDown" || e.key === "ArrowUp") && target.closest("table")) {
            const table = target.closest("table");
            const tableInputs = getNavigableFields(table);
            const idx = tableInputs.indexOf(target);
            if (idx >= 0) {
                e.preventDefault();
                const nextIdx = e.key === "ArrowDown" ? idx + 1 : idx - 1;
                if (nextIdx >= 0 && nextIdx < tableInputs.length) {
                    focusElement(tableInputs[nextIdx]);
                }
                return;
            }
        }

        // Enter key handling: move to next field instead of submitting/saving
        if (e.key === "Enter" && !e.ctrlKey && !e.altKey && !e.metaKey) {
            // Allow Shift+Enter inside textarea for newline
            if (isTextarea && e.shiftKey) {
                return;
            }

            e.preventDefault();
            e.stopPropagation();

            // Special case: inside fabric picker row (.picker-qty), trigger its Add button if quantity > 0
            if (target.classList.contains("picker-qty")) {
                const addBtn = target.parentElement?.querySelector(".picker-add:not(:disabled)");
                if (addBtn && Number(target.value) > 0) {
                    addBtn.click();
                    return;
                }
            }

            const scope = target.closest(".modal, .offcanvas, form, .card-body") || document.body;
            const fields = getNavigableFields(scope);
            const currentIndex = fields.indexOf(target);

            if (currentIndex >= 0 && currentIndex < fields.length - 1) {
                focusElement(fields[currentIndex + 1]);
            } else {
                // On the last field: move focus to the primary action button without auto-submitting
                const primaryBtn = scope.querySelector(
                    '#qualitySubmit:not(.d-none), #nfSave:not(.d-none), button[type="submit"]:not(:disabled)'
                );
                if (primaryBtn && primaryBtn.offsetParent !== null) {
                    primaryBtn.focus();
                } else if (fields.length > 0) {
                    focusElement(fields[0]);
                }
            }
        }
    }, true);

    // Auto-focus the first editable field whenever a modal or offcanvas opens
    document.addEventListener("shown.bs.modal", function (e) {
        const modal = e.target;
        if (!modal) return;
        const fields = getNavigableFields(modal);
        if (fields.length > 0) {
            focusElement(fields[0]);
        }
    });

    document.addEventListener("shown.bs.offcanvas", function (e) {
        const offcanvas = e.target;
        if (!offcanvas) return;
        const fields = getNavigableFields(offcanvas);
        if (fields.length > 0) {
            focusElement(fields[0]);
        }
    });
})();
