(function () {
  function initializeCatalogMap() {
    const mapElement = document.getElementById("catalog-map");
    if (!mapElement || typeof L === "undefined") {
      return;
    }

    if (mapElement.dataset.initialized === "true") {
      return;
    }

    const tileUrl = mapElement.dataset.tileUrl;
    const catalogName = mapElement.dataset.catalogName || "Catalog";
    const boundsJson = mapElement.dataset.bounds;
    if (!tileUrl || !boundsJson) {
      return;
    }

    let bounds;
    try {
      bounds = JSON.parse(boundsJson);
    } catch {
      return;
    }

    const opacityInput = document.getElementById("overlay-opacity");
    const initialOpacity = opacityInput ? Number.parseFloat(opacityInput.value) : 0.75;

    const map = L.map(mapElement, {
      scrollWheelZoom: true
    });

    const baseLayer = L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
    }).addTo(map);

    const overlayLayer = L.tileLayer(tileUrl, {
      maxZoom: 22,
      opacity: Number.isFinite(initialOpacity) ? initialOpacity : 0.75,
      attribution: `${catalogName} overlay`
    }).addTo(map);

    L.control.layers(
      {
        "OpenStreetMap": baseLayer
      },
      {
        [catalogName]: overlayLayer
      }
    ).addTo(map);

    if (Array.isArray(bounds) && bounds.length === 2) {
      map.fitBounds(bounds, { padding: [24, 24] });
    } else {
      map.setView([0, 0], 2);
    }

    if (opacityInput) {
      opacityInput.addEventListener("input", function () {
        const opacity = Number.parseFloat(opacityInput.value);
        if (Number.isFinite(opacity)) {
          overlayLayer.setOpacity(opacity);
        }
      });
    }

    mapElement.dataset.initialized = "true";
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeCatalogMap, { once: true });
  } else {
    initializeCatalogMap();
  }
})();
