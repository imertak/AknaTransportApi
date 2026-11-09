// AKNA Transport müşteri canlı takip sayfası.
// Bağlantı biçimi: https://<api-adresi>/takip/?t=<token> (eski bağlantılar için #<token> da desteklenir).
// Sunucu istek loglarına sorgu dizesi yazılmaz. Sayfa /api/tracking/<token> uç noktasını 15 saniyede bir sorgular.
(function () {
  'use strict';

  var REFRESH_MS = 15000;
  var STATUS = {
    Pending: 'Planlandı',
    Assigned: 'Şoföre atandı',
    Accepted: 'Hazırlanıyor',
    OnTheWay: 'Yolda',
    Loading: 'Yükleniyor',
    Delivering: 'Teslimata çıktı',
    Completed: 'Teslim edildi',
    Cancelled: 'İptal edildi'
  };
  var LIVE = ['Accepted', 'OnTheWay', 'Loading', 'Delivering'];

  var token = new URLSearchParams(location.search).get('t') || decodeURIComponent((location.hash || '').replace(/^#/, '')) || '';
  var el = function (id) { return document.getElementById(id); };
  var timer = null;
  var map = null;        // { setVehicle, clearVehicle, setDestination, setRoute, frame }
  var mapFailed = false;

  // Uygulamadaki harita ile aynı görünüm: MapLibre + OpenFreeMap vektör karoları ve uygulamanın teması
  // (harita-stili.json, uygulamadaki src/theme/map'ten üretilir). Tarayıcı WebGL desteklemiyorsa veya
  // stil yüklenemezse Esri'nin anahtarsız sokak haritasıyla (Leaflet) devam edilir.
  var ARROW = '<svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true"><polygon points="12 2 19 21 12 17 5 21 12 2" fill="#fff" stroke="#fff" stroke-width="1.5" stroke-linejoin="round"/></svg>';

  function vehicleElement() {
    var wrap = document.createElement('div');
    wrap.className = 'vehicle';
    wrap.innerHTML = '<div class="vehicle-body"><div class="vehicle-arrow">' + ARROW + '</div></div><div class="vehicle-label" hidden></div>';
    return wrap;
  }

  function updateVehicleElement(node, heading, plate) {
    var arrow = node.querySelector('.vehicle-arrow');
    // Yön bilinmiyorsa (duran araç) ok son yönünde kalır.
    if (typeof heading === 'number' && heading >= 0) arrow.style.transform = 'rotate(' + heading + 'deg)';
    var label = node.querySelector('.vehicle-label');
    label.textContent = plate || '';
    label.hidden = !plate;
  }

  function destinationElement() {
    var node = document.createElement('div');
    node.className = 'dest';
    node.setAttribute('aria-label', 'Teslimat noktası');
    return node;
  }

  var EMPTY_LINE = { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: [] } };

  function createMapLibre() {
    var gl = new maplibregl.Map({
      container: 'map',
      style: 'harita-stili.json',
      center: [28.979, 41.015],
      zoom: 10,
      attributionControl: { compact: true },
      dragRotate: false,
      pitchWithRotate: false
    });
    gl.touchZoomRotate.disableRotation();
    gl.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right');
    var marker = null, node = null, dest = null, loaded = false, pendingRoute = null;
    var toLngLat = function (p) { return [p[1], p[0]]; };
    function applyRoute(coords, straight) {
      var line = function (c) { return { type: 'Feature', properties: {}, geometry: { type: 'LineString', coordinates: c ? c.map(toLngLat) : [] } }; };
      gl.getSource('route').setData(line(straight ? null : coords));
      gl.getSource('route-straight').setData(line(straight ? coords : null));
    }
    gl.on('load', function () {
      loaded = true;
      // Rota: beyaz kenarlıklı koyu çizgi; gerçek yol bulunamazsa kesikli düz çizgi.
      gl.addSource('route', { type: 'geojson', data: EMPTY_LINE });
      gl.addSource('route-straight', { type: 'geojson', data: EMPTY_LINE });
      var round = { 'line-cap': 'round', 'line-join': 'round' };
      gl.addLayer({ id: 'route-casing', type: 'line', source: 'route', layout: round, paint: { 'line-color': '#FFFFFF', 'line-width': 9 } });
      gl.addLayer({ id: 'route-line', type: 'line', source: 'route', layout: round, paint: { 'line-color': '#1A1A1A', 'line-width': 4.5 } });
      gl.addLayer({ id: 'route-straight', type: 'line', source: 'route-straight', layout: { 'line-cap': 'round' }, paint: { 'line-color': '#1A1A1A', 'line-width': 3, 'line-dasharray': [1.5, 2] } });
      if (pendingRoute) { applyRoute(pendingRoute.coords, pendingRoute.straight); pendingRoute = null; }
    });
    gl.on('error', function () {
      // Stil (veya karo kaynağı) hiç yüklenemediyse yedek haritaya geç; tek tük karo hataları yok sayılır.
      if (!loaded && !mapFailed) { mapFailed = true; gl.remove(); switchToFallback(); }
    });
    return {
      setVehicle: function (pos, heading, plate) {
        if (!marker) {
          node = vehicleElement();
          marker = new maplibregl.Marker({ element: node, anchor: 'top', offset: [0, -21] }).setLngLat(toLngLat(pos)).addTo(gl);
        } else marker.setLngLat(toLngLat(pos));
        updateVehicleElement(node, heading, plate);
      },
      clearVehicle: function () {
        if (marker) { marker.remove(); marker = null; node = null; }
      },
      setDestination: function (pos) {
        if (!pos) { if (dest) { dest.remove(); dest = null; } return; }
        if (!dest) dest = new maplibregl.Marker({ element: destinationElement(), anchor: 'center' }).setLngLat(toLngLat(pos)).addTo(gl);
        else dest.setLngLat(toLngLat(pos));
      },
      setRoute: function (coords, straight) {
        if (loaded) applyRoute(coords, straight);
        else pendingRoute = { coords: coords, straight: straight };
      },
      frame: function (points) {
        if (points.length === 1) { gl.jumpTo({ center: toLngLat(points[0]), zoom: 14 }); return; }
        var b = new maplibregl.LngLatBounds();
        points.forEach(function (p) { b.extend(toLngLat(p)); });
        gl.fitBounds(b, { padding: { top: 70, bottom: 50, left: 50, right: 50 }, maxZoom: 15, duration: 0 });
      }
    };
  }

  function createLeaflet() {
    var lf = L.map('map', { zoomControl: true, attributionControl: true }).setView([41.015, 28.979], 10);
    // OpenStreetMap'in kendi karo sunucusu Referer göndermeyen sayfaları engelliyor, CARTO ise tanımadığı
    // adreslerden API anahtarı istiyor. Esri'nin anahtarsız sokak haritası her iki sorunu da yaşamaz.
    // Esri bir gün yanıt vermezse OpenStreetMap'e (site kökü Referer olarak gönderilerek) geçilir.
    var primary = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}', {
      maxZoom: 19,
      attribution: 'Harita &copy; Esri, HERE, Garmin, &copy; OpenStreetMap katkıcıları'
    }).addTo(lf);
    var errors = 0;
    primary.on('tileerror', function () {
      if (++errors !== 5) return;
      lf.removeLayer(primary);
      L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        referrerPolicy: 'strict-origin-when-cross-origin',
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>'
      }).addTo(lf);
    });
    var marker = null, node = null, dest = null, routeLayers = [];
    return {
      setVehicle: function (pos, heading, plate) {
        if (!marker) {
          node = vehicleElement();
          marker = L.marker(pos, { icon: L.divIcon({ className: '', html: node, iconSize: [96, 64], iconAnchor: [48, 21] }), zIndexOffset: 1000 }).addTo(lf);
        } else marker.setLatLng(pos);
        updateVehicleElement(node, heading, plate);
      },
      clearVehicle: function () {
        if (marker) { lf.removeLayer(marker); marker = null; node = null; }
      },
      setDestination: function (pos) {
        if (!pos) { if (dest) { lf.removeLayer(dest); dest = null; } return; }
        if (!dest) dest = L.marker(pos, { icon: L.divIcon({ className: '', html: destinationElement(), iconSize: [26, 26], iconAnchor: [13, 13] }) }).addTo(lf);
        else dest.setLatLng(pos);
      },
      setRoute: function (coords, straight) {
        routeLayers.forEach(function (l) { lf.removeLayer(l); });
        routeLayers = [];
        if (!coords || coords.length < 2) return;
        if (straight) {
          routeLayers.push(L.polyline(coords, { color: '#1A1A1A', weight: 3, dashArray: '4 8', lineCap: 'round' }).addTo(lf));
        } else {
          routeLayers.push(L.polyline(coords, { color: '#FFFFFF', weight: 9, lineCap: 'round', lineJoin: 'round' }).addTo(lf));
          routeLayers.push(L.polyline(coords, { color: '#1A1A1A', weight: 4.5, lineCap: 'round', lineJoin: 'round' }).addTo(lf));
        }
      },
      frame: function (points) {
        if (points.length === 1) lf.setView(points[0], 14);
        else lf.fitBounds(points, { paddingTopLeft: [50, 70], paddingBottomRight: [50, 50], maxZoom: 15 });
      }
    };
  }

  // Son bilinen durum: harita yedeğe geçerse veya rota sonradan gelirse yeniden çizilir.
  var state = { vehicle: null, dest: null, route: null, framed: '' };

  // Yol rotası: OSRM uyumlu servis (uygulamadaki ile aynı). Araç 150 m'den fazla yer değiştirmedikçe yeniden
  // istenmez; servis yanıt vermezse araçtan hedefe kesikli düz çizgi gösterilir.
  var ROUTING_URL = 'https://router.project-osrm.org';
  var REROUTE_METERS = 150;
  var routeRequest = { from: null, to: null, busy: false };

  function meters(a, b) {
    var r = Math.PI / 180, dLat = (b[0] - a[0]) * r, dLng = (b[1] - a[1]) * r;
    var h = Math.sin(dLat / 2) * Math.sin(dLat / 2) + Math.cos(a[0] * r) * Math.cos(b[0] * r) * Math.sin(dLng / 2) * Math.sin(dLng / 2);
    return 6371000 * 2 * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h));
  }

  function drawRoute() {
    if (!map) return;
    var v = state.vehicle && state.vehicle.pos, d = state.dest;
    if (!v || !d) { map.setRoute(null, false); return; }
    if (state.route) map.setRoute([v].concat(state.route), false);
    else map.setRoute([v, d], true);
  }

  function refreshRoute() {
    var v = state.vehicle && state.vehicle.pos, d = state.dest;
    if (!v || !d) { state.route = null; drawRoute(); return; }
    var sameTarget = !!routeRequest.to && routeRequest.to[0] === d[0] && routeRequest.to[1] === d[1];
    if (!sameTarget) state.route = null;
    var fresh = sameTarget && !!routeRequest.from && meters(routeRequest.from, v) < REROUTE_METERS;
    drawRoute();
    if (fresh || routeRequest.busy || typeof fetch !== 'function') return;
    routeRequest = { from: v, to: d, busy: true };
    var url = ROUTING_URL + '/route/v1/driving/' + v[1] + ',' + v[0] + ';' + d[1] + ',' + d[0] + '?overview=full&geometries=geojson&steps=false';
    fetch(url, { headers: { Accept: 'application/json' } })
      .then(function (res) { return res.ok ? res.json() : null; })
      .then(function (body) {
        var c = body && body.code === 'Ok' && body.routes && body.routes[0] && body.routes[0].geometry && body.routes[0].geometry.coordinates;
        state.route = c && c.length > 1 ? c.map(function (p) { return [p[1], p[0]]; }) : null;
      })
      .catch(function () { state.route = null; })
      .then(function () { routeRequest.busy = false; drawRoute(); });
  }

  /** Harita ilk kez (veya araç/hedef görünürlüğü değişince) araç ve hedefi birlikte gösterecek şekilde konumlanır. */
  function frameMap() {
    if (!map) return;
    var points = [];
    if (state.vehicle) points.push(state.vehicle.pos);
    if (state.dest) points.push(state.dest);
    var key = (state.vehicle ? 'v' : '') + (state.dest ? 'd' : '');
    if (!points.length || key === state.framed) return;
    state.framed = key;
    map.frame(points);
  }

  function redraw() {
    if (!map) return;
    if (state.vehicle) map.setVehicle(state.vehicle.pos, state.vehicle.heading, state.vehicle.plate);
    else map.clearVehicle();
    map.setDestination(state.dest);
    drawRoute();
    frameMap();
  }

  function switchToFallback() {
    map = null;
    state.framed = '';
    el('map').innerHTML = '';
    var css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
    document.head.appendChild(css);
    var js = document.createElement('script');
    js.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
    js.onload = function () {
      map = createLeaflet();
      redraw();
    };
    document.head.appendChild(js);
  }

  function initMap() {
    if (map || mapFailed) return;
    var webgl = typeof maplibregl !== 'undefined' && (typeof maplibregl.supported !== 'function' || maplibregl.supported());
    if (!webgl) { mapFailed = true; switchToFallback(); return; }
    try {
      map = createMapLibre();
    } catch (e) {
      mapFailed = true;
      switchToFallback();
    }
  }

  function formatDate(iso) {
    var parts = String(iso || '').split('-');
    return parts.length === 3 ? parts[2] + '.' + parts[1] + '.' + parts[0] : '';
  }

  function formatAgo(iso) {
    var seconds = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 1000));
    if (seconds < 60) return 'az önce güncellendi';
    var minutes = Math.round(seconds / 60);
    if (minutes < 60) return minutes + ' dk önce güncellendi';
    return new Date(iso).toLocaleString('tr-TR', { hour: '2-digit', minute: '2-digit', day: '2-digit', month: '2-digit' });
  }

  function showError(title, message) {
    el('status').textContent = 'Bağlantı';
    el('status').className = 'badge';
    el('title').textContent = title;
    el('message').textContent = message;
    el('details').hidden = true;
    el('open-maps').hidden = true;
    el('updated').textContent = '';
  }

  function render(data) {
    var status = data.status;
    var live = LIVE.indexOf(status) >= 0;
    el('status').textContent = STATUS[status] || status;
    el('status').className = 'badge' + (live && data.location ? ' live' : live ? ' idle' : '');
    // Mağaza adı "Firma /" gibi sonda ayraçla kaydedilmiş olabilir; başlıkta temizlenir.
    var store = String(data.storeName || '').replace(/[\s\/·-]+$/, '');
    el('title').textContent = (store ? store + ' · ' : '') + data.jobNumber;
    el('date').textContent = formatDate(data.jobDate);
    el('address').textContent = data.deliveryAddress || '-';
    el('plate').textContent = data.vehiclePlate || '';
    el('plate').hidden = el('plate-label').hidden = !data.vehiclePlate;
    el('details').hidden = false;

    var loc = data.location;
    var closed = status === 'Completed' || status === 'Cancelled';
    state.dest = !closed && typeof data.deliveryLatitude === 'number' && typeof data.deliveryLongitude === 'number'
      ? [data.deliveryLatitude, data.deliveryLongitude] : null;
    state.vehicle = loc ? { pos: [loc.latitude, loc.longitude], heading: loc.heading, plate: data.vehiclePlate || '' } : null;
    if (state.vehicle || state.dest) initMap();
    redraw();
    refreshRoute();
    if (loc) {
      el('updated').textContent = formatAgo(loc.recordedAt);
      el('message').textContent = 'Aracın konumu haritada canlı olarak gösterilir.';
      el('open-maps').href = 'https://www.google.com/maps/search/?api=1&query=' + loc.latitude + ',' + loc.longitude;
      el('open-maps').hidden = false;
    } else {
      el('updated').textContent = '';
      el('open-maps').hidden = true;
      el('message').textContent = status === 'Completed'
        ? 'Gönderiniz teslim edildi. Konum paylaşımı sona erdi.'
        : status === 'Cancelled'
          ? 'Bu iş iptal edildi.'
          : live
            ? 'Araç konumu bekleniyor. Sayfa otomatik yenilenir.'
            : 'Araç yola çıktığında konumu burada görünecek.';
    }

    // Kapanmış işte sorgulamaya gerek yok.
    if (status === 'Completed' || status === 'Cancelled') stop();
  }

  function stop() {
    if (timer) { clearInterval(timer); timer = null; }
  }

  function load() {
    fetch('/api/tracking/' + encodeURIComponent(token), { headers: { Accept: 'application/json' }, cache: 'no-store' })
      .then(function (res) {
        return res.json().catch(function () { return null; }).then(function (body) { return { res: res, body: body }; });
      })
      .then(function (r) {
        if (r.res.ok && r.body && r.body.data) { render(r.body.data); return; }
        if (r.res.status === 404) {
          stop();
          showError('Bağlantı geçersiz', 'Takip bağlantısının süresi dolmuş veya iptal edilmiş olabilir.');
        } else if (r.res.status === 429) {
          el('message').textContent = 'Çok sık yenilendi, birazdan tekrar denenecek.';
        } else {
          el('message').textContent = 'Bilgi alınamadı, tekrar denenecek.';
        }
      })
      .catch(function () {
        el('message').textContent = 'Bağlantı kurulamadı, tekrar denenecek.';
      });
  }

  initMap();
  if (!token || token.length < 20) {
    showError('Bağlantı eksik', 'Size gönderilen takip bağlantısını eksiksiz açın.');
    return;
  }
  load();
  timer = setInterval(load, REFRESH_MS);
  document.addEventListener('visibilitychange', function () {
    if (!document.hidden && timer) load();
  });
})();
