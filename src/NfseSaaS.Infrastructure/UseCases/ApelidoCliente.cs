using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Regras de Cliente.Apelido compartilhadas entre cadastro, edição e a
/// criação automática de Cliente na sincronização com a SEFIN (ADN).
///
/// Unicidade sem diferenciar maiúsculas/minúsculas ("Copenor" e
/// "COPENOR" são o mesmo apelido na tela), contada na LISTA de cada
/// empresa (C10): exclusivos dela + compartilhados que ela enxerga.
/// - exclusivo da empresa E: não pode repetir outro exclusivo de E nem um
///   compartilhado visível em E (o compartilhado com o MESMO documento fica
///   escondido em E por este exclusivo, então não conta);
/// - compartilhado: não pode repetir outro compartilhado nem um exclusivo
///   de qualquer empresa onde ele vai aparecer.
/// Os índices únicos do banco comparam exato e por escopo — são só o
/// backstop; a regra de verdade é esta.
/// </summary>
internal static class ApelidoCliente
{
    public static async Task GarantirUnicoAsync(
        AppDbContext db, Guid? empresaId, string apelido, string cpfCnpj, Guid? clienteIdIgnorado, CancellationToken cancellationToken)
    {
        if (await ExisteAsync(db, empresaId, apelido, cpfCnpj, clienteIdIgnorado, cancellationToken))
            throw new RegraNegocioException(empresaId is null
                ? $"Já existe um cliente com o apelido '{apelido}' numa das empresas da conta."
                : $"Já existe um cliente com o apelido '{apelido}' nesta empresa.");
    }

    /// <summary>
    /// Apelido para um Cliente criado sem intervenção do usuário: a
    /// própria razão social, cortada no tamanho máximo. Se já estiver em
    /// uso, acrescenta os 6 últimos dígitos do CPF/CNPJ (e, no limite, um
    /// contador) até ficar único. O usuário pode trocar depois no cadastro.
    /// </summary>
    public static async Task<string> GerarAPartirDoNomeAsync(
        AppDbContext db, Guid? empresaId, string nome, string cpfCnpj, CancellationToken cancellationToken)
    {
        var baseApelido = string.IsNullOrWhiteSpace(nome) ? cpfCnpj : nome.Trim();

        var candidato = Cortar(baseApelido, Cliente.TamanhoMaximoApelido);
        if (!await ExisteAsync(db, empresaId, candidato, cpfCnpj, null, cancellationToken))
            return candidato;

        var finalDocumento = cpfCnpj.Length > 6 ? cpfCnpj[^6..] : cpfCnpj;

        for (var tentativa = 1; tentativa <= 99; tentativa++)
        {
            var sufixo = tentativa == 1 ? $" {finalDocumento}" : $" {finalDocumento}-{tentativa}";
            candidato = Cortar(baseApelido, Cliente.TamanhoMaximoApelido - sufixo.Length).TrimEnd() + sufixo;

            if (!await ExisteAsync(db, empresaId, candidato, cpfCnpj, null, cancellationToken))
                return candidato;
        }

        throw new RegraNegocioException($"Não foi possível gerar um apelido único para o cliente '{nome}'.");
    }

    /// <summary>IM1: o apelido já está em uso na lista onde o cliente vai aparecer (conferência da planilha, antes de gravar).</summary>
    public static Task<bool> EmUsoAsync(AppDbContext db, Guid? empresaId, string apelido, string cpfCnpj, CancellationToken cancellationToken) =>
        ExisteAsync(db, empresaId, apelido, cpfCnpj, null, cancellationToken);

    private static async Task<bool> ExisteAsync(
        AppDbContext db, Guid? empresaId, string apelido, string cpfCnpj, Guid? clienteIdIgnorado, CancellationToken cancellationToken)
    {
        var apelidoMinusculo = apelido.ToLowerInvariant();

        // Clientes adicionados neste mesmo DbContext e ainda não salvos
        // (sincronização cria vários num lote só, com um SaveChanges no
        // final) — a consulta ao banco não os enxerga.
        var existeEmMemoria = db.Clientes.Local.Any(c =>
            c.EmpresaId == empresaId &&
            c.Id != clienteIdIgnorado &&
            string.Equals(c.Apelido, apelido, StringComparison.OrdinalIgnoreCase));

        if (existeEmMemoria)
            return true;

        var mesmoApelido = db.Clientes.Where(c =>
            (clienteIdIgnorado == null || c.Id != clienteIdIgnorado) &&
            c.Apelido.ToLower() == apelidoMinusculo);

        if (empresaId is Guid empresa)
        {
            // Exclusivo de E: outro exclusivo de E, ou compartilhado de outro
            // documento que E enxerga (E não tem exclusivo com o documento dele).
            return await mesmoApelido.AnyAsync(c =>
                c.EmpresaId == empresa ||
                (c.EmpresaId == null && c.CpfCnpj != cpfCnpj &&
                 !db.Clientes.Any(x => x.EmpresaId == empresa && x.CpfCnpj == c.CpfCnpj
                                       && (clienteIdIgnorado == null || x.Id != clienteIdIgnorado))),
                cancellationToken);
        }

        // Compartilhado: outro compartilhado, ou exclusivo de outro documento
        // numa empresa onde este vai aparecer (que não o esconde com um
        // exclusivo do mesmo documento).
        return await mesmoApelido.AnyAsync(c =>
            c.EmpresaId == null ||
            (c.CpfCnpj != cpfCnpj &&
             !db.Clientes.Any(x => x.EmpresaId == c.EmpresaId && x.CpfCnpj == cpfCnpj
                                   && (clienteIdIgnorado == null || x.Id != clienteIdIgnorado))),
            cancellationToken);
    }

    private static string Cortar(string texto, int tamanho) =>
        texto.Length <= tamanho ? texto : texto[..tamanho];
}
