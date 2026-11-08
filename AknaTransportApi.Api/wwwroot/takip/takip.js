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
  var map = null;        // { setVehicle(pos, heading, plate), clearVehicle() }
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
    var marker = null, node = null, centered = false, loaded = false;
    gl.on('load', function () { loaded = true; });
    gl.on('error', function (e) {
      // Stil (veya karo kaynağı) hiç yüklenemediyse yedek haritaya geç; tek tük karo hataları yok sayılır.
      if (!loaded && !mapFailed) { mapFailed = true; gl.remove(); switchToFallback(); }
    });
    return {
      setVehicle: function (pos, heading, plate) {
        var lngLat = [pos[1], pos[0]];
        if (!marker) {
          node = vehicleElement();
          marker = new maplibregl.Marker({ element: node, anchor: 'top', offset: [0, -21] }).setLngLat(lngLat).addTo(gl);
        } else marker.setLngLat(lngLat);
        updateVehicleElement(node, heading, plate);
        if (!centered) { gl.jumpTo({ center: lngLat, zoom: 14 }); centered = true; }
      },
      clearVehicle: function () {
        if (marker) { marker.remove(); marker = null; node = null; centered = false; }
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
    var marker = null, node = null, centered = false;
    return {
      setVehicle: function (pos, heading, plate) {
        if (!marker) {
          node = vehicleElement();
          marker = L.marker(pos, { icon: L.divIcon({ className: '', html: node, iconSize: [96, 64], iconAnchor: [48, 21] }) }).addTo(lf);
        } else marker.setLatLng(pos);
        updateVehicleElement(node, heading, plate);
        if (!centered) { lf.setView(pos, 14); centered = true; }
      },
      clearVehicle: function () {
        if (marker) { lf.removeLayer(marker); marker = null; node = null; centered = false; }
      }
    };
  }

  var lastVehicle = null;

  function switchToFallback() {
    map = null;
    el('map').innerHTML = '';
    var css = document.createElement('link');
    css.rel = 'stylesheet';
    css.href = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css';
    document.head.appendChild(css);
    var js = document.createElement('script');
    js.src = 'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js';
    js.onload = function () {
      map = createLeaflet();
      if (lastVehicle) map.setVehicle(lastVehicle.pos, lastVehicle.heading, lastVehicle.plate);
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
    if (loc) {
      var pos = [loc.latitude, loc.longitude];
      lastVehicle = { pos: pos, heading: loc.heading, plate: data.vehiclePlate || '' };
      initMap();
      if (map) map.setVehicle(pos, loc.heading, data.vehiclePlate || '');
      el('updated').textContent = formatAgo(loc.recordedAt);
      el('message').textContent = 'Aracın konumu haritada canlı olarak gösterilir.';
      el('open-maps').href = 'https://www.google.com/maps/search/?api=1&query=' + loc.latitude + ',' + loc.longitude;
      el('open-maps').hidden = false;
    } else {
      lastVehicle = null;
      if (map) map.clearVehicle();
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
