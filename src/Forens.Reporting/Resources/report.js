(function () {
  "use strict";

  function $(sel, root) { return (root || document).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); }

  document.addEventListener("DOMContentLoaded", function () {
    var search = $("#filter-text");
    var categoryButtons = $$(".filter-btn[data-category]");
    var statusButtons = $$(".filter-btn[data-status]");
    var sources = $$("article.source");

    var activeCategory = "all";
    var activeStatus = "all";

    function apply() {
      var q = (search && search.value || "").toLowerCase().trim();
      sources.forEach(function (el) {
        var cat = el.getAttribute("data-category");
        var status = el.getAttribute("data-status");
        var hay = (el.textContent || "").toLowerCase();
        var show =
          (activeCategory === "all" || activeCategory === cat) &&
          (activeStatus === "all" || activeStatus === status) &&
          (q === "" || hay.indexOf(q) !== -1);
        el.classList.toggle("hidden", !show);
      });
      $$("section.category").forEach(function (cat) {
        var anyVisible = $$("article.source", cat).some(function (a) {
          return !a.classList.contains("hidden");
        });
        cat.classList.toggle("hidden", !anyVisible);
      });
    }

    if (search) search.addEventListener("input", apply);
    categoryButtons.forEach(function (b) {
      b.addEventListener("click", function () {
        categoryButtons.forEach(function (x) { x.classList.remove("active"); });
        b.classList.add("active");
        activeCategory = b.getAttribute("data-category");
        apply();
      });
    });
    statusButtons.forEach(function (b) {
      b.addEventListener("click", function () {
        statusButtons.forEach(function (x) { x.classList.remove("active"); });
        b.classList.add("active");
        activeStatus = b.getAttribute("data-status");
        apply();
      });
    });

    apply();
  });
})();
