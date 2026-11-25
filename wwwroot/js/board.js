// JobHunt HQ — pipeline board interactions (phase 3).
//
// Two ways to move a card, one endpoint: drag-and-drop, and the status
// <select> on each card (the keyboard / screen-reader path — it performs
// exactly the same move). Both POST to Board/Move with the antiforgery
// token the page renders. The DOM only changes after the server confirms;
// on any failure the card stays where it was, the select snaps back, and
// the error note at the top of the board explains.
(function () {
    "use strict";

    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var errorBox = document.getElementById("board-error");
    if (!tokenInput) return;

    function showError(message) {
        if (!errorBox) return;
        errorBox.textContent = message;
        errorBox.hidden = false;
    }

    function hideError() {
        if (!errorBox) return;
        errorBox.hidden = true;
        errorBox.textContent = "";
    }

    function postMove(id, status) {
        var body = new URLSearchParams();
        body.set("id", id);
        body.set("status", status);
        body.set("__RequestVerificationToken", tokenInput.value);
        return fetch("/Board/Move", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: body.toString(),
            credentials: "same-origin"
        }).then(function (response) { return response.ok; })
          .catch(function () { return false; });
    }

    // Recompute one column's count, empty note, and WIP nudge from the DOM.
    function refreshColumn(column) {
        var allCards = column.querySelectorAll("[data-card]");
        var visibleCards = Array.from(allCards).filter(function (c) { return !c.hidden; });
        var countEl = column.querySelector("[data-count]");
        if (countEl) countEl.textContent = visibleCards.length;

        var container = column.querySelector("[data-cards]");
        var emptyNote = container.querySelector("[data-empty-note]");
        if (visibleCards.length === 0) {
            if (!emptyNote) {
                emptyNote = document.createElement("p");
                emptyNote.className = "empty-note board-empty";
                emptyNote.setAttribute("data-empty-note", "");
                emptyNote.textContent = "None";
                container.appendChild(emptyNote);
            }
            emptyNote.hidden = false;
        } else if (emptyNote) {
            emptyNote.hidden = true;
        }

        var wip = column.querySelector("[data-wip-note]");
        if (wip) {
            var limit = parseInt(wip.getAttribute("data-wip-limit"), 10);
            if (!isNaN(limit) && allCards.length > limit) {
                wip.textContent = allCards.length + " here \u2014 " + wip.getAttribute("data-wip-hint");
                wip.hidden = false;
            } else {
                wip.hidden = true;
            }
        }
    }

    function refreshAllColumns() {
        document.querySelectorAll("[data-column]").forEach(refreshColumn);
    }

    function columnForStatus(status) {
        return document.querySelector('[data-column][data-status="' + status + '"]');
    }

    // Move a card element into the target column after a confirmed move.
    function placeCard(card, status) {
        var column = columnForStatus(status);
        if (!column) return;
        column.querySelector("[data-cards]").appendChild(card);
        card.setAttribute("data-status", status);
        var select = card.querySelector("[data-move-select]");
        if (select) select.value = status;
        card.classList.remove("card-moved");
        void card.offsetWidth;
        card.classList.add("card-moved");
        refreshAllColumns();
    }

    function moveCard(card, status) {
        var current = card.getAttribute("data-status");
        if (status === current) return Promise.resolve(true);
        return postMove(card.getAttribute("data-id"), status).then(function (ok) {
            if (ok) {
                hideError();
                placeCard(card, status);
            } else {
                showError("Couldn't move that card \u2014 it stayed where it was. Check your connection and try again.");
                var select = card.querySelector("[data-move-select]");
                if (select) select.value = current;
            }
            return ok;
        });
    }

    // ---- The status select: same move, no drag required. ----
    document.querySelectorAll("[data-move-select]").forEach(function (select) {
        select.addEventListener("change", function () {
            var card = select.closest("[data-card]");
            if (card) moveCard(card, select.value);
        });
    });

    // ---- Drag and drop ----
    var draggedCard = null;

    document.querySelectorAll("[data-card]").forEach(function (card) {
        // Pressing on the select or the details link shouldn't start a drag —
        // those have their own job. Temporarily disarm dragging for them.
        card.addEventListener("mousedown", function (event) {
            card.draggable = !event.target.closest("select, a, button");
        });
        card.addEventListener("mouseup", function () { card.draggable = true; });

        card.addEventListener("dragstart", function (event) {
            if (event.target.closest && event.target.closest("select")) {
                event.preventDefault();
                return;
            }
            draggedCard = card;
            card.classList.add("dragging");
            event.dataTransfer.effectAllowed = "move";
            event.dataTransfer.setData("text/plain", card.getAttribute("data-id"));
        });
        card.addEventListener("dragend", function () {
            draggedCard = null;
            card.classList.remove("dragging");
            document.querySelectorAll("[data-column]").forEach(function (c) {
                c.classList.remove("drop-target");
            });
        });
    });

    document.querySelectorAll("[data-column]").forEach(function (column) {
        column.addEventListener("dragover", function (event) {
            if (!draggedCard) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = "move";
            column.classList.add("drop-target");
        });
        column.addEventListener("dragleave", function (event) {
            if (!column.contains(event.relatedTarget)) {
                column.classList.remove("drop-target");
            }
        });
        column.addEventListener("drop", function (event) {
            event.preventDefault();
            column.classList.remove("drop-target");
            if (!draggedCard) return;
            var card = draggedCard;
            draggedCard = null;
            moveCard(card, column.getAttribute("data-status"));
        });
    });

    // ---- Board toolbar filtering & hide-closed toggle ----
    var searchInput = document.getElementById("board-search");
    var fitSelect = document.getElementById("board-fit-filter");
    var hideClosedCheckbox = document.getElementById("board-hide-closed");

    function applyBoardFilters() {
        var term = (searchInput ? searchInput.value : "").trim().toLowerCase();
        var fit = fitSelect ? fitSelect.value : "";
        var hideClosed = hideClosedCheckbox ? hideClosedCheckbox.checked : false;

        document.querySelectorAll("[data-card]").forEach(function (card) {
            var cardText = card.getAttribute("data-text") || "";
            var cardFit = card.getAttribute("data-fit") || "not-checked";
            var matchesText = !term || cardText.indexOf(term) !== -1;
            var matchesFit = !fit || cardFit === fit;
            card.hidden = !(matchesText && matchesFit);
        });

        document.querySelectorAll("[data-column]").forEach(function (column) {
            var isClosed = column.getAttribute("data-closed") === "true";
            column.hidden = hideClosed && isClosed;
        });

        refreshAllColumns();
    }

    if (searchInput) searchInput.addEventListener("input", applyBoardFilters);
    if (fitSelect) fitSelect.addEventListener("change", applyBoardFilters);
    if (hideClosedCheckbox) hideClosedCheckbox.addEventListener("change", applyBoardFilters);
})();
