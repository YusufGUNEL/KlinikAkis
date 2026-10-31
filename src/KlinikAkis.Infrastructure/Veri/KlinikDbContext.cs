using KlinikAkis.Domain;
using KlinikAkis.Domain.Varliklar;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KlinikAkis.Infrastructure.Veri;

public sealed class KlinikDbContext(DbContextOptions<KlinikDbContext> options) : DbContext(options)
{
    public DbSet<Hasta> Hastalar => Set<Hasta>();
    public DbSet<Brans> Branslar => Set<Brans>();
    public DbSet<Hekim> Hekimler => Set<Hekim>();
    public DbSet<Slot> Slotlar => Set<Slot>();
    public DbSet<Randevu> Randevular => Set<Randevu>();
    public DbSet<Kabul> Kabuller => Set<Kabul>();
    public DbSet<OnAnamnez> OnAnamnezler => Set<OnAnamnez>();
    public DbSet<Muayene> Muayeneler => Set<Muayene>();
    public DbSet<TahlilTanimi> TahlilTanimlari => Set<TahlilTanimi>();
    public DbSet<TahlilSonucu> TahlilSonuclari => Set<TahlilSonucu>();
    public DbSet<Epikriz> Epikrizler => Set<Epikriz>();
    public DbSet<OutboxMesaji> OutboxMesajlari => Set<OutboxMesaji>();
    public DbSet<AiAuditLog> AiAuditLoglari => Set<AiAuditLog>();
    public DbSet<ErisimKaydi> ErisimKayitlari => Set<ErisimKaydi>();

    protected override void ConfigureConventions(ModelConfigurationBuilder yapilandirma)
    {
        // SQLite DateTime'ı Kind=Unspecified okur; tüm zamanlar UTC saklanır ve UTC döner.
        yapilandirma.Properties<DateTime>().HaveConversion<UtcDonusturucu>();

        // SQLite decimal'ı sıralayamaz/karşılaştıramaz (SPEC §5 notu); Postgres'te numeric kalır.
        if (Database.IsSqlite())
            yapilandirma.Properties<decimal>().HaveConversion<double>();

        // Enum'lar metin olarak saklanır: okunur ve enum sırası değişince veri bozulmaz.
        yapilandirma.Properties<Cinsiyet>().HaveConversion<string>().HaveMaxLength(1);
        yapilandirma.Properties<BransKodu>().HaveConversion<string>().HaveMaxLength(40);
        yapilandirma.Properties<RandevuDurumu>().HaveConversion<string>().HaveMaxLength(20);
        yapilandirma.Properties<OnAnamnezDurumu>().HaveConversion<string>().HaveMaxLength(20);
        yapilandirma.Properties<AiKaynak>().HaveConversion<string>().HaveMaxLength(10);
        yapilandirma.Properties<Isaret>().HaveConversion<string>().HaveMaxLength(20);
        yapilandirma.Properties<MuayeneDurumu>().HaveConversion<string>().HaveMaxLength(10);
        yapilandirma.Properties<EpikrizDurumu>().HaveConversion<string>().HaveMaxLength(20);
        yapilandirma.Properties<PaketTipi>().HaveConversion<string>().HaveMaxLength(20);
        yapilandirma.Properties<OutboxDurumu>().HaveConversion<string>().HaveMaxLength(20);
        yapilandirma.Properties<AiOzellik>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Hasta>(e =>
        {
            e.Property(x => x.TcKimlik).HasMaxLength(11).IsFixedLength();
            e.HasIndex(x => x.TcKimlik).IsUnique();
            e.Property(x => x.TakmaAd).HasMaxLength(10);
            e.HasIndex(x => x.TakmaAd).IsUnique();
        });

        model.Entity<Brans>().HasIndex(x => x.Kod).IsUnique();

        model.Entity<Hekim>()
            .HasOne(x => x.Brans).WithMany().HasForeignKey(x => x.BransId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<Slot>(e =>
        {
            e.HasOne(x => x.Hekim).WithMany().HasForeignKey(x => x.HekimId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HekimId, x.Baslangic }).IsUnique();
        });

        model.Entity<Randevu>(e =>
        {
            e.HasOne(x => x.Hasta).WithMany().HasForeignKey(x => x.HastaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Hekim).WithMany().HasForeignKey(x => x.HekimId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Slot).WithMany().HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Restrict);
            // Bir slotta en fazla bir etkin randevu; iptal edilen randevu slotu bırakır (yeniden alınabilir).
            // Filtre SQLite ve Postgres'te aynı yazılır; enum metin olarak saklanıyor.
            e.HasIndex(x => x.SlotId).IsUnique().HasFilter("\"Durum\" <> 'Iptal'");
            // Durum yalnızca durum makinesiyle değişir; eşzamanlı iki geçişten ikincisi reddedilir (oku-kontrol-yaz).
            e.Property(x => x.Durum).IsConcurrencyToken();
            e.HasOne<OnAnamnez>().WithMany().HasForeignKey(x => x.OnAnamnezId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<Kabul>(e =>
        {
            e.HasOne<Randevu>().WithOne().HasForeignKey<Kabul>(x => x.RandevuId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TakipNo).HasMaxLength(20);
            e.HasIndex(x => x.TakipNo).IsUnique();
        });

        model.Entity<OnAnamnez>(e =>
        {
            e.HasOne<Hasta>().WithMany().HasForeignKey(x => x.HastaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Hekim>().WithMany().HasForeignKey(x => x.OnaylayanHekimId).OnDelete(DeleteBehavior.Restrict);
            // RandevuId ↔ Randevu.OnAnamnezId döngüsel FK olmasın diye RandevuId yalnızca değer olarak tutulur.
        });

        model.Entity<Muayene>()
            .HasOne<Randevu>().WithOne().HasForeignKey<Muayene>(x => x.RandevuId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<TahlilTanimi>().HasIndex(x => x.LoincKodu).IsUnique();

        model.Entity<TahlilSonucu>(e =>
        {
            e.HasOne<Hasta>().WithMany().HasForeignKey(x => x.HastaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.TahlilTanimi).WithMany().HasForeignKey(x => x.TahlilTanimiId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HastaId, x.TahlilTanimiId, x.Zaman });
        });

        model.Entity<Epikriz>(e =>
        {
            e.HasOne<Muayene>().WithMany().HasForeignKey(x => x.MuayeneId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Hekim>().WithMany().HasForeignKey(x => x.OnaylayanHekimId).OnDelete(DeleteBehavior.Restrict);
            // Muayene başına en fazla bir reddedilmemiş epikriz: eşzamanlı iki "oluştur" iki taslak yazamaz.
            e.HasIndex(x => x.MuayeneId).IsUnique().HasFilter("\"Durum\" <> 'Reddedildi'");
            // Eşzamanlı iki karardan ikincisi birincinin üzerine yazmasın (oku-kontrol-yaz, Randevu ile aynı).
            e.Property(x => x.Durum).IsConcurrencyToken();
        });

        model.Entity<ErisimKaydi>()
            .HasOne<Hasta>().WithMany().HasForeignKey(x => x.HastaId).OnDelete(DeleteBehavior.Restrict);
    }

    private sealed class UtcDonusturucu() : ValueConverter<DateTime, DateTime>(
        v => UtcYaz(v),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
        // Local bir değer etiket değiştirilerek kaydedilirse veri sessizce 3 saat kayar; hata ver.
        private static DateTime UtcYaz(DateTime v) => v.Kind switch
        {
            DateTimeKind.Local => throw new InvalidOperationException("DateTime UTC olmalı; Kind=Local kaydedilemez."),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(v, DateTimeKind.Utc),
            _ => v,
        };
    }
}
