// Tela "Indique e ganhe": link da conta, atalhos de envio e as contas indicadas.
document.addEventListener('DOMContentLoaded', async function () {
    const campo = document.getElementById('link-indicacao');
    const lista = document.getElementById('lista-indicacoes');

    let dados;
    try {
        dados = await apiFetch('/api/indicacao');
    } catch (err) {
        campo.value = '';
        lista.innerHTML = '<li class="text-danger">Não foi possível carregar.</li>';
        mostrarErro(err.message);
        return;
    }

    campo.value = dados.link;
    const mensagem = `Uso o micoempresa para emitir as notas fiscais de serviço: a nota mensal sai sozinha e o cliente recebe por e-mail. Crie sua conta grátis: ${dados.link}`;
    document.getElementById('btn-whatsapp').href = `https://wa.me/?text=${encodeURIComponent(mensagem)}`;
    document.getElementById('btn-email').href =
        `mailto:?subject=${encodeURIComponent('Emissor de nota fiscal de serviço')}&body=${encodeURIComponent(mensagem)}`;

    document.getElementById('btn-copiar-link').addEventListener('click', async function () {
        try {
            await navigator.clipboard.writeText(dados.link);
            mostrarToast('Link copiado.');
        } catch {
            campo.select();
            mostrarToast('Selecione e copie o link com Ctrl+C.');
        }
    });

    document.getElementById('total-indicacoes').textContent = dados.indicadas.length;
    if (dados.indicadas.length === 0) {
        lista.innerHTML = '<li class="text-muted">Ninguém se cadastrou pelo seu link ainda.</li>';
        return;
    }

    lista.innerHTML = dados.indicadas.map(function (i) {
        const data = new Date(i.cadastradaEm).toLocaleDateString('pt-BR');
        const situacao = !i.ativa
            ? '<span class="badge text-bg-secondary">Conta suspensa</span>'
            : i.jaEmitiuNota
                ? '<span class="badge text-bg-success">Já emite notas</span>'
                : '<span class="badge text-bg-warning">Configurando</span>';
        return `<li class="d-flex justify-content-between align-items-center py-2 border-bottom"><span>Conta criada em ${data}</span>${situacao}</li>`;
    }).join('');
});
