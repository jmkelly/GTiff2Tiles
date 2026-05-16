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
    const opacityValueDisplay = document.getElementById("opacity-value");
    const initialOpacity = opacityInput ? Number.parseFloat(opacityInput.value) : 0.75;

    const map = L.map(mapElement, {
      scrollWheelZoom: true,
      zoomControl: true
    });

    const baseLayer = L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
    }).addTo(map);

    const overlayLayer = L.tileLayer(tileUrl, {
      maxZoom: 22,
      opacity: Number.isFinite(initialOpacity) ? initialOpacity : 0.75,
      attribution: catalogName
    }).addTo(map);

    L.control.layers(
      {
        "OSM": baseLayer
      },
      {
        [catalogName]: overlayLayer
      },
      { collapsed: true }
    ).addTo(map);

    if (Array.isArray(bounds) && bounds.length === 2) {
      map.fitBounds(bounds, { padding: [32, 32] });
    } else {
      map.setView([0, 0], 2);
    }

    if (opacityInput) {
      opacityInput.addEventListener("input", function () {
        const opacity = Number.parseFloat(opacityInput.value);
        if (Number.isFinite(opacity)) {
          overlayLayer.setOpacity(opacity);
          if (opacityValueDisplay) {
            opacityValueDisplay.textContent = opacity.toFixed(2);
          }
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
