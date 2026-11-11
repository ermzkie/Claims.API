using System;
using System.Collections.Generic;
using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Data;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Address> Addresses { get; set; }

    public virtual DbSet<Assistance> Assistances { get; set; }

    public virtual DbSet<Barangay> Barangays { get; set; }

    public virtual DbSet<CityMunicipality> CityMunicipalities { get; set; }

    public virtual DbSet<CivilStatus> CivilStatuses { get; set; }

    public virtual DbSet<Creditor> Creditors { get; set; }

    public virtual DbSet<FamilyComposition> FamilyCompositions { get; set; }

    public virtual DbSet<FundSource> FundSources { get; set; }

    public virtual DbSet<ModeOfAssistance> ModeOfAssistances { get; set; }

    public virtual DbSet<NameSuffix> NameSuffixes { get; set; }

    public virtual DbSet<NatureOfService> NatureOfServices { get; set; }

    public virtual DbSet<Officer> Officers { get; set; }

    public virtual DbSet<Person> People { get; set; }

    public virtual DbSet<Province> Provinces { get; set; }

    public virtual DbSet<RelationshipStatus> RelationshipStatuses { get; set; }

    public virtual DbSet<Role> Roles { get; set; }

    public virtual DbSet<Sex> Sexes { get; set; }

    public virtual DbSet<SourceOfAssistance> SourceOfAssistances { get; set; }

    public virtual DbSet<Transaction> Transactions { get; set; }

    public virtual DbSet<TransactionService> TransactionServices { get; set; }

    public virtual DbSet<TransactionsBackup20261109> TransactionsBackup20261109s { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<UserRole> UserRoles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Address>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<Assistance>(entity =>
        {
            entity.Property(e => e.AssistanceId).ValueGeneratedOnAdd();
            entity.Property(e => e.IsActive).HasDefaultValue(true, "DF_Assistance_isActive");
        });

        modelBuilder.Entity<Barangay>(entity =>
        {
            entity.Property(e => e.BarangayId).ValueGeneratedNever();

            entity.HasOne(d => d.CityMunicipality).WithMany(p => p.Barangays).HasConstraintName("FK_Barangay_CityMunicipality");
        });

        modelBuilder.Entity<CityMunicipality>(entity =>
        {
            entity.Property(e => e.CityMunicipalityId).ValueGeneratedNever();

            entity.HasOne(d => d.Province).WithMany(p => p.CityMunicipalities)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CityMunicipality_Province");
        });

        modelBuilder.Entity<CivilStatus>(entity =>
        {
            entity.Property(e => e.CivilStatusId).ValueGeneratedNever();
        });

        modelBuilder.Entity<FamilyComposition>(entity =>
        {
            entity.Property(e => e.DateCreated).HasDefaultValueSql("(getdate())", "DF_FamilyComposition_DateCreated");
            entity.Property(e => e.Status).HasDefaultValue(true, "DF_FamilyComposition_FamilyCompStatus");

            entity.HasOne(d => d.FamilyPerson).WithMany(p => p.FamilyCompositionFamilyPeople).HasConstraintName("FK_FamilyComposition_Person1");

            entity.HasOne(d => d.Person).WithMany(p => p.FamilyCompositionPeople)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_FamilyComposition_Person");

            entity.HasOne(d => d.RelationshipStatus).WithMany(p => p.FamilyCompositions).HasConstraintName("FK_FamilyComposition_RelationshipStatus");
        });

        modelBuilder.Entity<NameSuffix>(entity =>
        {
            entity.Property(e => e.NameSuffixId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Officer>(entity =>
        {
            entity.Property(e => e.Idnumber).ValueGeneratedNever();
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasKey(e => e.PersonId).HasName("PK_Patients");

            entity.Property(e => e.PersonId).ValueGeneratedNever();
            entity.Property(e => e.DateCreated).HasDefaultValueSql("(getdate())", "DF_Person_DateCreated");
            entity.Property(e => e.Status).HasDefaultValue(true, "DF_Patient_PatientStatus");

            entity.HasOne(d => d.BarangayNavigation).WithMany(p => p.People).HasConstraintName("FK_Person_Barangay");

            entity.HasOne(d => d.CivilStatus).WithMany(p => p.People).HasConstraintName("FK_Patient_CivilStatus");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.People).HasConstraintName("FK_Person_User");

            entity.HasOne(d => d.NameSuffix).WithMany(p => p.People).HasConstraintName("FK_Person_NameSuffix");

            entity.HasOne(d => d.Sex).WithMany(p => p.People).HasConstraintName("FK_Person_Sex");
        });

        modelBuilder.Entity<Province>(entity =>
        {
            entity.Property(e => e.ProvinceId).ValueGeneratedNever();
        });

        modelBuilder.Entity<RelationshipStatus>(entity =>
        {
            entity.Property(e => e.RelationshipStatusId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.Property(e => e.TransactionId).ValueGeneratedNever();
            entity.Property(e => e.DateCreated).HasDefaultValueSql("(getdate())", "DF_Transactions_DateCreated");
            entity.Property(e => e.Status).HasDefaultValue(true, "DF_Transactions_isActive");
            entity.Property(e => e.IsReleased).HasDefaultValue(false, "DF_Transactions_IsReleased");

            entity.HasOne(d => d.Creditor).WithMany(p => p.Transactions).HasConstraintName("FK_Transactions_Creditor");

            entity.HasOne(d => d.Mode).WithMany(p => p.Transactions).HasConstraintName("FK_Transactions_ModeOfAssistance");

            entity.HasOne(d => d.PatientPerson).WithMany(p => p.TransactionPatientPeople).HasConstraintName("FK_Transactions_Person");

            entity.HasOne(d => d.RepresentativePerson).WithMany(p => p.TransactionRepresentativePeople).HasConstraintName("FK_Transactions_Representative");

            entity.HasOne(d => d.FundSource).WithMany(p => p.Transactions).HasConstraintName("FK_Transactions_FundSource");

            entity.HasOne(d => d.SocialWorkerUser).WithMany()
                .HasForeignKey(d => d.SocialWorkerUserId)
                .HasConstraintName("FK_Transactions_SocialWorkerUser");
        });

        modelBuilder.Entity<FundSource>(entity =>
        {
            entity.Property(e => e.FundSourceId).ValueGeneratedNever();
            entity.Property(e => e.DateEn).IsFixedLength();
            entity.Property(e => e.Stat).IsFixedLength();

            entity.HasOne(d => d.Source).WithMany(p => p.FundSources).HasConstraintName("FK_FundSource_SourceOfAssistance");
        });

        modelBuilder.Entity<TransactionService>(entity =>
        {
            entity.Property(e => e.TranServiceId).ValueGeneratedNever();

            entity.HasOne(d => d.Nature).WithMany(p => p.TransactionServices).HasConstraintName("FK_TransactionServices_NatureOfService");

            entity.HasOne(d => d.Transaction).WithMany(p => p.TransactionServices)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TransactionServices_Transactions");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.DateCreated).HasDefaultValueSql("(getdate())", "DF_User_DateCreated");
            entity.Property(e => e.Status).HasDefaultValue(true, "DF_User_IsActive");

            entity.HasOne(d => d.NameSuffix).WithMany(p => p.Users).HasConstraintName("FK_User_NameSuffix");
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.Property(e => e.DateCreated).HasDefaultValueSql("(getdate())", "DF_UserRole_DateCreated");
            entity.Property(e => e.Status).HasDefaultValue(true, "DF_UserRole_Status");

            entity.HasOne(d => d.Role).WithMany(p => p.UserRoles).HasConstraintName("FK_UserRole_Role");

            entity.HasOne(d => d.User).WithMany(p => p.UserRoles).HasConstraintName("FK_UserRole_User");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
