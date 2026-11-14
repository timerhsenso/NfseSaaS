using NfseSaaS.Application.Common;
using Serilog.Core;
using Serilog.Events;

namespace NfseSaaS.Web.Monitoramento;

/// <summary>
/// SEG1 — tira segredos de todo evento de log antes de qualquer saída
/// (arquivo, console, e-mail de alerta): propriedade com nome sensível vira
/// "***" e texto com senha/token/CPF/CNPJ dentro é mascarado (ver
/// <see cref="MascaramentoLog"/>). Vale também para objetos destruturados
/// ({@Pedido}) e listas. Registrado em Program.cs logo depois do LogContext.
/// A mensagem renderizada usa as propriedades, então sai mascarada também.
/// </summary>
public sealed class MascaramentoLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (nome, valor) in logEvent.Properties.ToList())
        {
            var mascarado = Mascarar(nome, valor);
            if (!ReferenceEquals(mascarado, valor))
                logEvent.AddOrUpdateProperty(new LogEventProperty(nome, mascarado));
        }
    }

    /// <summary>O mesmo valor (mesma referência) quando nada precisou mudar.</summary>
    internal static LogEventPropertyValue Mascarar(string nome, LogEventPropertyValue valor)
    {
        if (MascaramentoLog.NomeSensivel(nome))
            return new ScalarValue(MascaramentoLog.Mascara);

        switch (valor)
        {
            case ScalarValue { Value: string texto }:
                var novo = MascaramentoLog.MascararTexto(texto);
                return novo == texto ? valor : new ScalarValue(novo);

            case StructureValue estrutura:
            {
                var mudou = false;
                var propriedades = estrutura.Properties.Select(p =>
                {
                    var interno = Mascarar(p.Name, p.Value);
                    mudou |= !ReferenceEquals(interno, p.Value);
                    return new LogEventProperty(p.Name, interno);
                }).ToList();
                return mudou ? new StructureValue(propriedades, estrutura.TypeTag) : valor;
            }

            case SequenceValue sequencia:
            {
                var mudou = false;
                var itens = sequencia.Elements.Select(e =>
                {
                    var interno = Mascarar(string.Empty, e);
                    mudou |= !ReferenceEquals(interno, e);
                    return interno;
                }).ToList();
                return mudou ? new SequenceValue(itens) : valor;
            }

            case DictionaryValue dicionario:
            {
                var mudou = false;
                var pares = dicionario.Elements.Select(par =>
                {
                    var chave = par.Key.Value?.ToString() ?? string.Empty;
                    var interno = Mascarar(chave, par.Value);
                    mudou |= !ReferenceEquals(interno, par.Value);
                    return new KeyValuePair<ScalarValue, LogEventPropertyValue>(par.Key, interno);
                }).ToList();
                return mudou ? new DictionaryValue(pares) : valor;
            }

            default:
                return valor;
        }
    }
}
