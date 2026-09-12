using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Identity;

namespace NfseSaaS.Infrastructure.Persistence.Configurations;

/// <summary>
/// AspNetUsers é Identity padrão (sem Configuration própria até agora) —
/// esta classe existe só pela FK nova de GrupoId, que precisa de uma
/// regra de exclusão explícita.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // SetNull, não Restrict/Cascade: excluir um Grupo com usuário
        // vinculado não pode ser bloqueado só por isso (ver
        // ExcluirGrupoUseCase, que já valida isso explicitamente antes
        // de deixar excluir) nem apagar o usuário em cascata — o pior
        // caso é o usuário ficar temporariamente sem grupo, tratado como
        // "sem nenhuma permissão" até um Administrador atribuir outro.
        builder.HasOne<Grupo>()
            .WithMany()
            .HasForeignKey(u => u.GrupoId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
