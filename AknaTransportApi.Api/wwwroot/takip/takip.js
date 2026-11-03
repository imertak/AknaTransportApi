// AKNA Transport müşteri canlı takip sayfası.
// Bağlantı biçimi: https://<api-adresi>/takip/#<token>. Token adres çubuğunun # kısmında olduğu için
// sunucu loglarına yazılmaz. Sayfa /api/tracking/<token> uç noktasını 15 saniyede bir sorgular.
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

  var token = decodeURIComponent((location.hash || '').replace(/^#/, '')) || new URLSearchParams(location.search).get('t') || '';
  var el = function (id) { return document.getElementById(id); };
  var map = null;
  var marker = null;
  var centered = false;
  var timer = null;

  function initMap() {
    if (map || typeof L === 'undefined') return;
    map = L.map('map', { zoomControl: true, attributionControl: true }).setView([41.015, 28.979], 10);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap'
    }).addTo(map);
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
    el('title').textContent = (data.storeName ? data.storeName + ' · ' : '') + data.jobNumber;
    el('date').textContent = formatDate(data.jobDate);
    el('address').textContent = data.deliveryAddress || '-';
    el('details').hidden = false;

    var loc = data.location;
    if (loc) {
      var pos = [loc.latitude, loc.longitude];
      initMap();
      if (map) {
        if (!marker) marker = L.marker(pos, { icon: L.divIcon({ className: '', html: '<div class="truck"></div>', iconSize: [24, 24], iconAnchor: [12, 12] }) }).addTo(map);
        else marker.setLatLng(pos);
        if (!centered) { map.setView(pos, 14); centered = true; }
      }
      el('updated').textContent = formatAgo(loc.recordedAt);
      el('message').textContent = 'Aracın konumu haritada canlı olarak gösterilir.';
      el('open-maps').href = 'https://www.google.com/maps/search/?api=1&query=' + loc.latitude + ',' + loc.longitude;
      el('open-maps').hidden = false;
    } else {
      if (marker && map) { map.removeLayer(marker); marker = null; centered = false; }
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
