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
/// "COPENOR" são o mesmo apelido na tela). O índice único do banco
/// compara exato — é só o backstop; a regra de verdade é esta.
/// </summary>
internal static class ApelidoCliente
{
    public static async Task GarantirUnicoAsync(
        AppDbContext db, Guid empresaId, string apelido, Guid? clienteIdIgnorado, CancellationToken cancellationToken)
    {
        if (await ExisteAsync(db, empresaId, apelido, clienteIdIgnorado, cancellationToken))
            throw new RegraNegocioException($"Já existe um cliente com o apelido '{apelido}' nesta empresa.");
    }

    /// <summary>
    /// Apelido para um Cliente criado sem intervenção do usuário: a
    /// própria razão social, cortada no tamanho máximo. Se já estiver em
    /// uso, acrescenta os 6 últimos dígitos do CPF/CNPJ (e, no limite, um
    /// contador) até ficar único. O usuário pode trocar depois no cadastro.
    /// </summary>
    public static async Task<string> GerarAPartirDoNomeAsync(
        AppDbContext db, Guid empresaId, string nome, string cpfCnpj, CancellationToken cancellationToken)
    {
        var baseApelido = string.IsNullOrWhiteSpace(nome) ? cpfCnpj : nome.Trim();

        var candidato = Cortar(baseApelido, Cliente.TamanhoMaximoApelido);
        if (!await ExisteAsync(db, empresaId, candidato, null, cancellationToken))
            return candidato;

        var finalDocumento = cpfCnpj.Length > 6 ? cpfCnpj[^6..] : cpfCnpj;

        for (var tentativa = 1; tentativa <= 99; tentativa++)
        {
            var sufixo = tentativa == 1 ? $" {finalDocumento}" : $" {finalDocumento}-{tentativa}";
            candidato = Cortar(baseApelido, Cliente.TamanhoMaximoApelido - sufixo.Length).TrimEnd() + sufixo;

            if (!await ExisteAsync(db, empresaId, candidato, null, cancellationToken))
                return candidato;
        }

        throw new RegraNegocioException($"Não foi possível gerar um apelido único para o cliente '{nome}'.");
    }

    private static async Task<bool> ExisteAsync(
        AppDbContext db, Guid empresaId, string apelido, Guid? clienteIdIgnorado, CancellationToken cancellationToken)
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

        return await db.Clientes.AnyAsync(c =>
            c.EmpresaId == empresaId &&
            (clienteIdIgnorado == null || c.Id != clienteIdIgnorado) &&
            c.Apelido.ToLower() == apelidoMinusculo,
            cancellationToken);
    }

    private static string Cortar(string texto, int tamanho) =>
        texto.Length <= tamanho ? texto : texto[..tamanho];
}
