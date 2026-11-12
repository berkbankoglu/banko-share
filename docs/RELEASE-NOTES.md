## Banko Share 0.4.2: Riot oyunları için oyun modu

Kurulu sürüm bu güncellemeyi kendiliğinden alır. Hemen almak için **Uygulama → Şimdi kontrol et** seçin.

### Yeni

- **Oyun modu (Riot Vanguard uyumu):** VALORANT veya League of Legends açıkken, yani Riot Vanguard'ın oyun hizmeti çalışırken Banko Share o bilgisayarda tamamen kenara çekilir:
  - Fare ve klavye kancalarını kaldırır; oyunun girişini hiçbir şekilde dinlemez ya da engellemez.
  - O bilgisayara hiçbir yapay giriş (SendInput) göndermez. Başka bir bilgisayar kontrol ediyorsa imleci ona geri yollar ve basılı tuşları bırakır.
  - Diğer bilgisayara oraya geçmemesini bildirir. Durum karşı tarafta da "oyun modunda" olarak görünür.
  - Oyunun sürecine dokunmaz: sürecini açmaz, belleğini okumaz. Oyunu tanımak için yalnızca Windows'un hizmet ve süreç listelerine bakar.
  - Oyun kapanınca kendiliğinden devam eder.
  - Varsayılan olarak açıktır; **Uygulama → Oyunlar ve hile önleme** bölümünden yönetilir.
- "Bu programlar öndeyken geçme" ayarı artık başka programların süreçlerini hiç açmıyor; program adlarını Windows'un süreç listesinden okuyor.

Banko Share sürücü kurmaz ve çekirdek düzeyinde çalışmaz. Riot, üçüncü taraf giriş programları için yayımlanmış bir uyumluluk kuralı listesi sunmuyor. Bu mod, korumalı bir oyun açıkken Banko Share'in o bilgisayardaki girişle ilgili her şeyden uzak durmasını sağlar.

Bu bir geliştirme önizlemesidir. Windows kod imzalama sertifikası henüz yoktur.
