// TR1: tour guiado das telas (Driver.js). Abre sozinho na primeira vez que o
// usuário entra em cada tela (o "já visto" fica no servidor, por usuário:
// api/tour) e pode ser revisto pelo menu do usuário.
//
// Cada passo aponta para um elemento da tela; passo cujo elemento não existe
// ou está escondido (permissão, tela vazia) é pulado. No celular, os passos
// do menu lateral viram um só, no botão que abre o menu.
(function () {
    'use strict';

    const CELULAR = window.matchMedia('(max-width: 992px)');
    const ATRASO_AUTOMATICO_MS = 1500; // espera as listas carregarem

    const menu = href => `#sidebar a[href="${href}"]`;

    /** Conteúdo dos tours. Código = o mesmo de RegrasTour.Codigos (servidor). */
    const TOURS = {
        painel: {
            rota: ['/', '/home', '/home/index'],
            passos: () => [
                {
                    titulo: 'Bem-vindo ao micoempresa 👋',
                    texto: 'Um passeio rápido para você saber onde fica cada coisa. Leva menos de um minuto e dá para rever quando quiser no menu do seu usuário (canto de cima, à direita).'
                },
                {
                    elemento: '#seletor-empresa-topo',
                    titulo: 'Empresa selecionada',
                    texto: 'Tudo o que você vê e emite vale para a empresa escolhida aqui. Tem mais de uma? Troque por este campo.'
                },
                {
                    elemento: '#badge-ambiente-empresa',
                    titulo: 'Homologação ou Produção',
                    texto: '<strong>Homologação</strong> é o ambiente de testes da SEFIN: a nota não vale como nota fiscal. <strong>Produção</strong> é para valer. Você troca no cadastro da empresa.'
                },
                {
                    elemento: '#card-onboarding',
                    titulo: 'Primeiros passos',
                    texto: 'Siga esta lista na ordem: empresa, certificado digital, serviço, cliente e a primeira nota.'
                },
                { elemento: menu('/Nfse'), menu: 'Notas fiscais', titulo: 'Notas fiscais', texto: 'Emita, consulte, baixe o PDF/XML e mande a nota por e-mail para o cliente.' },
                { elemento: menu('/Contratos'), menu: 'Contratos', titulo: 'Contratos', texto: 'Cadastre o que você cobra todo mês. O sistema calcula o reajuste no aniversário e prepara a Nota Mensal.' },
                { elemento: menu('/Clientes'), menu: 'Clientes', titulo: 'Clientes', texto: 'Quem recebe as suas notas, com o e-mail para o envio automático e as cópias.' },
                { elemento: menu('/Empresas'), menu: 'Empresas', titulo: 'Empresas', texto: 'Seus dados de emitente e o certificado digital A1, que a SEFIN exige para emitir.' },
                {
                    elemento: '#painel-financeiro .pf-indicadores',
                    titulo: 'Seu negócio',
                    texto: 'Faturado no mês, receita dos contratos, o que falta emitir e o limite do MEI ou a faixa do Simples. Aparece assim que houver nota em produção ou contrato.'
                },
                ...(obterEmpresaAtualIdSeguro() ? [] : [{
                    elemento: menu('/Empresas'),
                    menu: 'Empresas',
                    titulo: 'Comece por aqui',
                    texto: 'Cadastre sua empresa: com o CNPJ, o sistema busca os dados para você.'
                }])
            ]
        },
        notas: {
            rota: ['/nfse'],
            passos: () => [
                { elemento: '#btn-emitir-nfse', titulo: 'Emitir nota', texto: 'Escolha o cliente e o serviço; o valor e os impostos vêm do cadastro. Antes de enviar à SEFIN aparece um resumo para conferir.' },
                { elemento: '#btn-nota-mensal', titulo: 'Nota Mensal', texto: 'Todos os contratos do mês de uma vez: confira a lista e emita em lote.' },
                { elemento: '#filtros-nfse', titulo: 'Filtros', texto: 'Período, situação, cliente e se o cliente já confirmou o recebimento do e-mail.' },
                { elemento: '#tabela-nfse', titulo: 'Suas notas', texto: 'O envelope ao lado do cliente mostra o e-mail: azul = enviado; verde = o cliente confirmou que recebeu. Nas ações (⋯) você baixa, reenvia, substitui ou cancela.' },
                { elemento: '#check-selecionar-todas-nfse', titulo: 'Várias de uma vez', texto: 'Marque as notas para baixar tudo num ZIP ou mandar os e-mails em lote.' }
            ]
        },
        clientes: {
            rota: ['/clientes'],
            passos: () => [
                { elemento: '#btn-novo-cliente', titulo: 'Novo cliente', texto: 'Com o CNPJ, os dados vêm da Receita. Na seção "Envio da nota" você informa o e-mail, as cópias e o modelo de e-mail.' },
                { elemento: '#btn-importar-clientes', titulo: 'Importar planilha', texto: 'Tem muitos clientes? Traga todos de uma vez, do Excel ou de outro sistema. Você confere antes de gravar.' },
                { elemento: '#tabela-clientes', titulo: 'Seus clientes', texto: 'Pesquise pelo nome, apelido ou documento. Nas ações você edita ou inativa.' }
            ]
        },
        contratos: {
            rota: ['/contratos'],
            passos: () => [
                { elemento: '#btn-novo-contrato', titulo: 'Novo contrato', texto: 'Cliente, serviços, valor, dia de faturamento e o índice de reajuste (IPCA, IGP-M…).' },
                { elemento: '#btn-importar-contratos', titulo: 'Importar planilha', texto: 'Traga os contratos (e os clientes) de uma planilha. Os serviços precisam estar cadastrados.' },
                { elemento: '#btn-verificar-reajustes', titulo: 'Reajustes', texto: 'O sistema confere os aniversários sozinho; aqui você força a conferência agora.' },
                { elemento: '#tabela-contratos', titulo: 'Seus contratos', texto: 'No ⋯ de cada contrato: histórico de valores, simular reajuste pelo índice e documentos.' },
                { elemento: '#totais-contratos', titulo: 'Totais', texto: 'A receita mensal dos contratos ativos e o total da lista filtrada.' }
            ]
        },
        servicos: {
            rota: ['/servicos'],
            passos: () => [
                { elemento: '#btn-novo-servico', titulo: 'Novo serviço', texto: 'O que você vende, com o código da lista de serviços (LC 116) e o código de tributação. Feito uma vez, vale para todas as notas.' },
                { elemento: '#tabela-servicos', titulo: 'Seus serviços', texto: 'Use o mesmo serviço em várias notas e contratos.' }
            ]
        },
        empresas: {
            rota: ['/empresas'],
            passos: () => [
                { elemento: '#btn-nova-empresa', titulo: 'Nova empresa', texto: 'Informe o CNPJ e o sistema busca os dados na Receita.' },
                { elemento: '#tabela-empresas', titulo: 'Certificado e ambiente', texto: 'No ⋯ da empresa: envie o certificado digital A1 (arquivo .pfx e senha) e teste a conexão com a SEFIN. Depois dos testes em Homologação, mude para Produção no cadastro.' }
            ]
        },
        documentos: {
            rota: ['/documentos'],
            passos: () => [
                { elemento: '#btn-escolher-documentos', titulo: 'Escolher documentos', texto: 'Marque os documentos que a sua empresa controla (certidões, contrato social…). Há pacotes prontos por ramo.' },
                { elemento: '#btn-novo-documento', titulo: 'Enviar documento', texto: 'Guarde o PDF com a validade. O sistema avisa 15 dias antes de vencer.' },
                { elemento: '#lista-documentos', titulo: 'Seus documentos', texto: 'Cada documento mostra se está em dia e o link oficial para tirar a versão nova. Os clientes podem exigir que eles vão junto com a nota.' }
            ]
        },
        importar: {
            rota: ['/importar'],
            passos: () => [
                { elemento: '#btn-baixar-modelo', titulo: 'Modelo', texto: 'Baixe o modelo com as abas Clientes e Contratos. Também serve a planilha de outro sistema.' },
                { elemento: '#importar-zona', titulo: 'Sua planilha', texto: 'Arraste o arquivo aqui (.xlsx ou .csv). Com o CNPJ, o nome e o endereço que faltarem vêm da Receita.' },
                { elemento: '#importar-atualizar', titulo: 'Já cadastrados', texto: 'Quem já está cadastrado não é duplicado. Marque aqui para atualizar com as células preenchidas.' },
                { elemento: '#btn-conferir', titulo: 'Conferir', texto: 'Você vê cada linha (nova, atualiza, já existe ou com erro) antes de gravar. Nada é gravado sem você confirmar.' }
            ]
        },
        'modelos-email': {
            rota: ['/modelosemail'],
            passos: () => [
                { elemento: '#lista-modelos', titulo: 'Seus modelos', texto: 'O padrão vale para todos; um cliente ou empresa pode usar outro.' },
                { elemento: '#modelo-campos', titulo: 'Campos', texto: 'Clique para inserir [Cliente], [Número da nota], [Valor]… Eles viram os dados de cada nota no envio. [Confirmar recebimento] vira o botão para o cliente confirmar que recebeu.' },
                { elemento: '#previa-corpo', titulo: 'Prévia', texto: 'Veja como o e-mail fica antes de salvar.' },
                { elemento: '#btn-teste-modelo', titulo: 'Testar', texto: 'Mande um e-mail de teste para você, com dados de exemplo.' }
            ]
        }
    };

    function obterEmpresaAtualIdSeguro() {
        try { return typeof obterEmpresaAtualId === 'function' ? obterEmpresaAtualId() : 'sem-controle'; } catch { return 'sem-controle'; }
    }

    function codigoDaTela() {
        const caminho = location.pathname.replace(/\/+$/, '').toLowerCase() || '/';
        return Object.keys(TOURS).find(codigo => TOURS[codigo].rota.includes(caminho)) || null;
    }

    function visivel(seletor) {
        const el = document.querySelector(seletor);
        if (!el) return false;
        const r = el.getBoundingClientRect();
        return (r.width > 0 || r.height > 0) && getComputedStyle(el).visibility !== 'hidden' && !el.closest('.d-none');
    }

    /** Passos que valem nesta tela agora (elemento visível; menu agrupado no celular). */
    function montarPassos(codigo) {
        const brutos = TOURS[codigo].passos();
        const passos = [];
        let menusCelular = [];

        const fecharGrupoMenu = () => {
            if (!menusCelular.length) return;
            passos.push({
                element: '#botaoMenu',
                popover: {
                    title: 'Menu',
                    description: `Toque aqui para abrir o menu: ${menusCelular.map(escaparHtmlTour).join(', ')}.`
                }
            });
            menusCelular = [];
        };

        for (const p of brutos) {
            if (p.menu && CELULAR.matches) {
                if (document.querySelector(p.elemento)) menusCelular.push(p.menu);
                continue;
            }
            fecharGrupoMenu();
            if (p.elemento && !visivel(p.elemento)) continue;
            passos.push({
                element: p.elemento,
                popover: { title: p.titulo, description: p.texto }
            });
        }
        fecharGrupoMenu();
        return passos;
    }

    function escaparHtmlTour(texto) {
        return typeof escaparHtml === 'function' ? escaparHtml(texto) : String(texto);
    }

    let vistos = null;

    async function carregarVistos() {
        if (vistos) return vistos;
        try {
            vistos = new Set((await apiFetch('/api/tour')) || []);
        } catch {
            vistos = null; // sem resposta: não abre sozinho (evita abrir toda hora)
        }
        return vistos;
    }

    async function marcarVisto(codigo) {
        vistos?.add(codigo);
        try { await apiFetch(`/api/tour/${encodeURIComponent(codigo)}/visto`, { method: 'POST' }); } catch { /* tenta de novo na próxima */ }
    }

    /** Começa o tour da tela (ou o indicado). Devolve false se não há tour aqui. */
    function iniciarTour(codigo) {
        codigo = codigo || codigoDaTela();
        const biblioteca = window.driver?.js?.driver;
        if (!codigo || !biblioteca) return false;
        const passos = montarPassos(codigo);
        if (!passos.length) return false;

        let marcado = false;
        const tour = biblioteca({
            steps: passos,
            showProgress: passos.length > 1,
            progressText: '{{current}} de {{total}}',
            nextBtnText: 'Próximo',
            prevBtnText: 'Voltar',
            doneBtnText: 'Concluir',
            popoverClass: 'tour-mico',
            overlayOpacity: 0.55,
            stagePadding: 6,
            stageRadius: 8,
            smoothScroll: false, // rolagem suave dentro do menu lateral deixava o destaque no lugar errado
            allowClose: true,
            onDestroyed: () => {
                if (!marcado) { marcado = true; marcarVisto(codigo); }
            }
        });
        tour.drive();
        return true;
    }

    window.iniciarTour = iniciarTour;

    function configurarMenuUsuario() {
        const link = document.getElementById('link-tour-tela');
        const rever = document.getElementById('link-rever-tours');
        if (link) {
            if (!codigoDaTela()) link.closest('li')?.classList.add('d-none');
            link.addEventListener('click', e => { e.preventDefault(); iniciarTour(); });
        }
        rever?.addEventListener('click', async e => {
            e.preventDefault();
            try {
                await apiFetch('/api/tour', { method: 'DELETE' });
                vistos = new Set();
                if (typeof mostrarToast === 'function')
                    mostrarToast('Pronto: o tour de cada tela vai abrir de novo na próxima vez que você entrar nela.');
                iniciarTour();
            } catch (err) {
                if (typeof mostrarToast === 'function') mostrarToast(err.message || 'Não foi possível agora.', 'erro');
            }
        });
    }

    async function automatico() {
        const codigo = codigoDaTela();
        if (!codigo) return;
        const lista = await carregarVistos();
        if (!lista || lista.has(codigo)) return;
        setTimeout(() => {
            // Não atrapalha quem já abriu um modal ou está digitando.
            if (document.querySelector('.modal.show, .offcanvas.show')) return;
            iniciarTour(codigo);
        }, ATRASO_AUTOMATICO_MS);
    }

    document.addEventListener('DOMContentLoaded', () => {
        configurarMenuUsuario();
        automatico();
    });
})();
