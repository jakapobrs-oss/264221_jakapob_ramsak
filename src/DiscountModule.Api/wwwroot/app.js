// The page at /: builds the JSON order the API accepts from the form, posts it and shows the answer.
// It holds no pricing logic; anything the API rejects is shown as the API reports it.
(() => {
  "use strict";

  const ENDPOINT = "/api/discounts";
  const CATEGORIES = ["Clothing", "Accessories", "Electronics"];
  const $ = (id) => document.getElementById(id);

  // The worked examples of the assignment (see README), plus the combined order from the design notes.
  const PRESETS = {
    fixed: {
      note: "Example 1: two items and a 50 THB coupon. Expected final price 550.00.",
      items: [["T-Shirt", "Clothing", 350], ["Hat", "Accessories", 250]],
      coupon: ["FixedAmount", { amount: 50 }],
    },
    percentage: {
      note: "Example 2: the same items and a 10% coupon. Expected final price 540.00.",
      items: [["T-Shirt", "Clothing", 350], ["Hat", "Accessories", 250]],
      coupon: ["Percentage", { percentage: 10 }],
    },
    category: {
      note: "Example 3: 15% off Clothing only (T-Shirt and Hoodie). Expected final price 2,382.50.",
      items: [["T-Shirt", "Clothing", 350], ["Hoodie", "Clothing", 700], ["Watch", "Electronics", 850], ["Bag", "Accessories", 640]],
      ontop: ["CategoryPercentage", { category: "Clothing", percentage: 15 }],
    },
    points: {
      note: "Example 4: 68 points, within the 20% cap. Expected final price 762.00.",
      items: [["T-Shirt", "Clothing", 350], ["Hat", "Accessories", 250], ["Belt", "Accessories", 230]],
      ontop: ["Points", { points: 68 }],
    },
    seasonal: {
      note: "Example 5: 40 THB off every 300 THB of 830. Expected final price 750.00.",
      items: [["T-Shirt", "Clothing", 350], ["Hat", "Accessories", 250], ["Belt", "Accessories", 230]],
      seasonal: ["Seasonal", { everyAmount: 300, discountAmount: 40 }],
    },
    all: {
      note: "One campaign of each category on the 830 THB cart: 10% coupon, 100 points, then 40 off every 300. Expected final price 567.00.",
      items: [["T-Shirt", "Clothing", 350], ["Hat", "Accessories", 250], ["Belt", "Accessories", 230]],
      coupon: ["Percentage", { percentage: 10 }],
      ontop: ["Points", { points: 100 }],
      seasonal: ["Seasonal", { everyAmount: 300, discountAmount: 40 }],
    },
  };

  // Which form controls hold the parameters of each campaign type, and the JSON property each one feeds.
  const PARAMETERS = {
    FixedAmount: { amount: "coupon-amount" },
    Percentage: { percentage: "coupon-percentage" },
    CategoryPercentage: { category: "ontop-item-category", percentage: "ontop-percentage" },
    Points: { points: "ontop-points-count" },
    Seasonal: { everyAmount: "seasonal-every", discountAmount: "seasonal-discount" },
  };

  // ---- items -------------------------------------------------------------------------------------

  function addItem([name, category, unitPrice, quantity] = ["", "Clothing", "", ""]) {
    const row = document.createElement("tr");

    // On a narrow screen the table turns into one block per item; each cell then shows its own heading.
    const cellLabels = ["Name", "Category", "Unit price (THB)", "Quantity", ""];
    const nextCell = () => {
      const cell = row.insertCell();
      cell.dataset.label = cellLabels[row.cells.length - 1];
      return cell;
    };

    const nameCell = nextCell();
    const nameInput = Object.assign(document.createElement("input"), { type: "text", value: name, className: "name" });
    nameCell.append(nameInput);

    const categoryCell = nextCell();
    const categorySelect = Object.assign(document.createElement("select"), { className: "category" });
    for (const option of CATEGORIES) {
      categorySelect.append(new Option(option, option, false, option === category));
    }
    categoryCell.append(categorySelect);

    const priceCell = nextCell();
    priceCell.className = "num";
    const priceInput = Object.assign(document.createElement("input"), { type: "number", className: "unitPrice", inputMode: "decimal" });
    priceInput.step = "0.01";
    priceInput.value = unitPrice === "" ? "" : String(unitPrice);
    priceCell.append(priceInput);

    const quantityCell = nextCell();
    quantityCell.className = "num";
    const quantityInput = Object.assign(document.createElement("input"), { type: "number", className: "quantity", inputMode: "numeric", placeholder: "1" });
    quantityInput.step = "1";
    quantityInput.value = quantity === undefined || quantity === "" ? "" : String(quantity);
    quantityCell.append(quantityInput);

    const removeCell = nextCell();
    const removeButton = Object.assign(document.createElement("button"), { type: "button", className: "remove", textContent: "Remove" });
    removeButton.addEventListener("click", () => {
      row.remove();
      labelItems();
      orderChanged();
    });
    removeCell.append(removeButton);

    $("items").append(row);
    labelItems();
    return nameInput;
  }

  // Each control names its row for assistive technology: "Item 2 name", "Item 2 unit price", ...
  function labelItems() {
    [...$("items").rows].forEach((row, index) => {
      const number = index + 1;
      row.querySelector(".name").setAttribute("aria-label", `Item ${number} name`);
      row.querySelector(".category").setAttribute("aria-label", `Item ${number} category`);
      row.querySelector(".unitPrice").setAttribute("aria-label", `Item ${number} unit price`);
      row.querySelector(".quantity").setAttribute("aria-label", `Item ${number} quantity`);
      row.querySelector(".remove").setAttribute("aria-label", `Remove item ${number}`);
    });
  }

  // ---- the order -----------------------------------------------------------------------------------

  // An empty field is left out, so the API reports what is missing. A number the browser could not parse
  // is sent as the text that was typed, so the API reports that too.
  function valueOf(control) {
    const text = control.value.trim();
    if (text === "") {
      return undefined;
    }

    if (control.tagName === "SELECT") {
      return text;
    }

    const number = Number(text);
    return Number.isFinite(number) ? number : text;
  }

  function withValue(target, property, control) {
    const value = valueOf(control);
    if (value !== undefined) {
      target[property] = value;
    }

    return target;
  }

  function buildOrder() {
    const items = [...$("items").rows].map((row) => {
      const item = {};
      withValue(item, "name", row.querySelector(".name"));
      withValue(item, "category", row.querySelector(".category"));
      withValue(item, "unitPrice", row.querySelector(".unitPrice"));
      withValue(item, "quantity", row.querySelector(".quantity"));
      return item;
    });

    const campaigns = [];
    for (const group of ["coupon", "ontop", "seasonal"]) {
      const type = document.querySelector(`input[name="${group}"]:checked`).value;
      if (type === "") {
        continue;
      }

      const campaign = { type };
      for (const [property, id] of Object.entries(PARAMETERS[type])) {
        withValue(campaign, property, $(id));
      }

      campaigns.push(campaign);
    }

    return { items, campaigns };
  }

  function updateRequestJson() {
    $("request-json").textContent = JSON.stringify(buildOrder(), null, 2);
  }

  // A result belongs to the order it was computed from: once the order changes, the old result is dimmed
  // and the status says so until the order is priced again.
  function markStale() {
    const result = $("result");
    if (result.dataset.priced === "true" && result.dataset.stale !== "true") {
      result.dataset.stale = "true";
      setStatus("stale", "Order changed. Price the order again to update the result.");
    }
  }

  // An example that was edited is no longer that example, so its note stops promising a result.
  function markEdited() {
    const note = $("preset-note");
    if (note.dataset.preset === "true") {
      note.textContent = "Custom order (edited after loading an example).";
      note.dataset.preset = "false";
    }
  }

  // Every edit of the order, typed or clicked, goes through here.
  function orderChanged() {
    updateRequestJson();
    markStale();
    markEdited();
  }

  // ---- money ---------------------------------------------------------------------------------------

  // The API writes every amount with 2 decimals. Where the browser lets a reviver see the source text of a
  // number, that text is kept as it is: an amount near the module's limit of 1,000,000,000,000,000.00 THB
  // has more digits than a JavaScript number holds, and the page must show what the API answered.
  function parseKeepingDigits(text) {
    return JSON.parse(text, (key, value, context) =>
      typeof value === "number" && typeof context?.source === "string" ? context.source : value);
  }

  // Groups the digits of an amount ("2382.50" or 2382.5) as "2,382.50" without any arithmetic on it.
  function formatMoney(amount) {
    const [whole, fraction = ""] = String(amount).split(".");
    const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ",");
    return `${grouped}.${(fraction + "00").slice(0, 2)}`;
  }

  // ---- pricing -------------------------------------------------------------------------------------

  async function price() {
    const button = $("price");
    button.disabled = true;
    button.textContent = "Pricing…";
    setStatus("busy", "Pricing…");
    $("result").setAttribute("aria-busy", "true");

    try {
      const response = await fetch(ENDPOINT, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(buildOrder()),
      });
      const text = await response.text();
      showResponseJson(text);

      const contentType = response.headers.get("Content-Type") ?? "";
      let data = null;
      if (/\bjson\b/i.test(contentType)) {
        try {
          data = parseKeepingDigits(text);
        } catch {
          data = null;
        }
      }

      if (response.ok && data !== null && Array.isArray(data.discounts)) {
        renderPriced(data);
      } else if (!response.ok && data !== null) {
        renderProblem(response.status, data);
      } else {
        renderFailure(`The API answered with HTTP ${response.status}, but not with the JSON this page expects.`);
      }
    } catch (error) {
      renderFailure(`The API could not be reached (${error.message}). Is the server running?`);
    } finally {
      button.disabled = false;
      button.textContent = "Price the order";
      $("result").removeAttribute("aria-busy");
    }
  }

  function setStatus(state, text) {
    const status = $("result-status");
    status.dataset.state = state;
    status.textContent = text;
  }

  function show(section) {
    for (const id of ["result-empty", "result-ok", "result-problem"]) {
      $(id).hidden = id !== section;
    }
  }

  function showResponseJson(text) {
    let pretty = text;
    try {
      pretty = JSON.stringify(JSON.parse(text), null, 2);
    } catch {
      // Not JSON: shown as it came.
    }

    $("response-json").textContent = pretty;
    $("response-details").hidden = false;
  }

  function renderPriced(result) {
    setStatus("ok", "Priced (HTTP 200)");
    $("subtotal").textContent = formatMoney(result.subtotal);
    $("total-discount").textContent = formatMoney(result.totalDiscount);
    $("final-price").textContent = formatMoney(result.finalPrice);
    $("points-used").textContent = String(result.pointsUsed);

    const body = $("breakdown");
    body.replaceChildren();
    const steps = result.discounts;
    steps.forEach((step, index) => {
      const row = body.insertRow();

      // A row header names the step: "1 · Coupon", the campaign, then the description the API wrote.
      const stepCell = document.createElement("th");
      stepCell.scope = "row";
      row.append(stepCell);
      const heading = document.createElement("div");
      heading.className = "step-name";
      heading.textContent = `${index + 1} · ${categoryName(step.category)} — ${String(step.campaign)}`;
      const description = document.createElement("div");
      description.className = "step-description";
      description.textContent = String(step.description);
      stepCell.append(heading, description);

      for (const amount of [formatMoney(step.priceBefore), `−${formatMoney(step.discountAmount)}`, formatMoney(step.priceAfter)]) {
        const cell = row.insertCell();
        cell.className = "num";
        cell.textContent = amount;
      }
    });

    $("no-campaigns").hidden = steps.length > 0;
    show("result-ok");
    $("result").dataset.priced = "true";
    delete $("result").dataset.stale;
  }

  function categoryName(category) {
    return category === "OnTop" ? "On Top" : String(category);
  }

  function renderProblem(status, problem) {
    setStatus("problem", `${problem.title ?? "The order was not priced"} (HTTP ${status})`);
    $("problem-title").textContent = problemExplanation(status);

    const list = $("problem-errors");
    list.replaceChildren();
    for (const message of Array.isArray(problem.errors) ? problem.errors : []) {
      const item = document.createElement("li");
      item.textContent = String(message);
      list.append(item);
    }

    list.hidden = list.childElementCount === 0;
    show("result-problem");
    $("result").dataset.priced = "true";
    delete $("result").dataset.stale;
  }

  function problemExplanation(status) {
    switch (status) {
      case 400: return "The order could not be read. Each line names the JSON path that is wrong:";
      case 413: return "The order is larger than the API accepts.";
      case 415: return "The API accepts only application/json.";
      case 422: return "The order was read, but it breaks these rules of the module:";
      default: return "The API did not price the order.";
    }
  }

  function renderFailure(message) {
    setStatus("failed", "Not priced");
    $("problem-title").textContent = message;
    $("problem-errors").replaceChildren();
    $("problem-errors").hidden = true;
    show("result-problem");
  }

  // ---- presets -------------------------------------------------------------------------------------

  function loadPreset(name) {
    const preset = PRESETS[name];
    $("items").replaceChildren();
    for (const item of preset.items) {
      addItem(item);
    }

    for (const group of ["coupon", "ontop", "seasonal"]) {
      const [type, parameters] = preset[group] ?? ["", {}];
      document.querySelector(`input[name="${group}"][value="${type}"]`).checked = true;
      for (const [property, value] of Object.entries(parameters)) {
        $(PARAMETERS[type][property]).value = String(value);
      }
    }

    updateRequestJson();
    markStale();
    $("preset-note").textContent = preset.note;
    $("preset-note").dataset.preset = "true";
  }

  // ---- wiring --------------------------------------------------------------------------------------

  document.querySelector(".preset-buttons").addEventListener("click", (event) => {
    const button = event.target.closest("button[data-preset]");
    if (button) {
      loadPreset(button.dataset.preset);
    }
  });

  $("add-item").addEventListener("click", () => {
    addItem().focus();
    orderChanged();
  });

  $("price").addEventListener("click", price);

  const order = document.querySelector(".order");
  // Typing a parameter selects the campaign it belongs to, so a value is never entered and then ignored.
  order.addEventListener("focusin", (event) => {
    const radio = event.target.dataset?.for;
    if (radio) {
      $(radio).checked = true;
      updateRequestJson();
    }
  });
  for (const eventName of ["input", "change"]) {
    order.addEventListener(eventName, orderChanged);
  }
  order.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && event.target.matches("input")) {
      event.preventDefault();
      price();
    }
  });

  loadPreset("all");
  price();
})();
