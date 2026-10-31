// WebApplicationFactory host oluşturma süreci (minimal hosting) süreç genelindeki bir DiagnosticListener
// üzerinden yürür; birden fazla factory'nin aynı anda başlatılması host'ları karıştırabilir.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
