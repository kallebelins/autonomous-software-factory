# Autonomous Software Factory

Fábrica de software autônoma orientada por agentes de IA. Transforma um requisito de negócio em código funcional, validado e entregue via Pull Request — sem intervenção humana no fluxo principal.

---

## Como funciona

```
Requisito → Análise do Projeto → Backlog → Microtarefas → Specs → Código → Build → Testes → PR
```

O sistema lê **workflows declarativos em YAML** que definem a sequência de execução. Cada step do workflow chama um **agente especializado** que usa um LLM para raciocinar e **tools determinísticas** para executar ações reais.

---

## Conceito

* **YAML define o workflow** — a sequência de steps, agentes, inputs e outputs
* **Agentes pensam** — usam LLM com prompts especializados por tarefa
* **Tools executam** — ações reais como git, build, testes, lint
* **Estado explícito** — cada step salva seu output para o próximo consumir

---

## Pipeline (workflow.yaml)

| # | Step | Agente | O que faz |
|---|------|--------|-----------|
| 1 | Receive Requirement | — | Valida entrada |
| 2 | Analyze Project | ProjectAnalyzerAgent | Identifica stack e arquitetura |
| 3 | Load Codebase | CodebaseLoaderAgent | Clona repositório |
| 4 | Analyze Codebase | CodebaseAnalyzerAgent | Lê e resume o código |
| 5 | Generate Backlog | BacklogAgent | Gera features |
| 6 | Breakdown Tasks | TaskBreakdownAgent | Quebra em microtarefas |
| 7 | Generate Specs | SpecAgent | Gera plan e tasks técnicas |
| 8 | Validate Architecture | ArchitectureValidatorAgent | Valida aderência |
| 9 | Setup Environment | EnvironmentSetupAgent | Instala dependências |
| 10 | Implement Code | DeveloperAgent | Implementa código |
| 11 | Execute Build | BuildAgent | Compila projeto |
| 12 | Execute Tests | TestAgent | Roda testes |
| 13 | Validate Quality | QualityAgent | Lint e análise estática |
| 14 | Review Delivery | ReviewAgent | Revisão final |
| 15 | Create Pull Request | PullRequestAgent | Branch, commit e PR |

---

## Estrutura do projeto

```
autonomous-software-factory/
├── configs/           # YAMLs declarativos (workflow, agents, skills, tools, prompts)
├── samples/           # Arquivos de entrada para execução local
├── docs/              # Documentação de referência
├── src/               # Código-fonte .NET (projeto único)
├── tests/             # Testes
├── workspace/         # Área temporária (repos clonados, artefatos)
├── logs/              # Logs de execução
└── scripts/           # Scripts de automação
```

Detalhamento completo em [project-structure.md](project-structure.md).

---

## Stack

* **.NET 8 (C#)** — projeto console único
* **YAML** — definição declarativa do workflow, agentes, skills e tools
* **LLM** — OpenAI ou compatível para raciocínio dos agentes
* **Git/GitHub** — versionamento e Pull Request

---

## Configs (YAML)

| Arquivo | Conteúdo |
|---------|----------|
| `workflow.yaml` | Steps do pipeline, inputs/outputs, políticas de retry |
| `agents.yaml` | Agentes com skills, tools e prompts |
| `skills_registry.yaml` | Capacidades cognitivas e operacionais |
| `tools.yaml` | Ferramentas executáveis (git, build, test, lint) |
| `prompts.yaml` | Prompts centralizados e templates de output |

---

## Como rodar (futuro)

```bash
# Clonar o projeto
git clone https://github.com/kallebelins/yaml-agents-orchestrator.git
cd autonomous-software-factory

# Executar
dotnet run --project src/AutonomousSoftwareFactory -- --requirement ./samples/requirement-sample.json
```

---

## Entrada

O requisito é fornecido via arquivo JSON:

```json
{
  "title": "Criar endpoint de cadastro de usuário",
  "description": "API REST para cadastro com validação de email",
  "repository": {
    "url": "https://github.com/owner/project",
    "branch": "main"
  }
}
```

---

## Fases de implementação

1. **Fundação** — parser YAML, models, state store, workflow engine
2. **Execução** — LLM client, agent executor, tool executor
3. **Integração** — git operations, build, testes
4. **Completo** — pipeline end-to-end, samples testáveis

---

## 🔍 Codebase Processing

O sistema suporta dois cenários:

### ✔ Projeto existente

* Clona repositório
* Analisa estrutura
* Identifica arquitetura
* Evita sobrescrever código existente

### ✔ Projeto novo

* Usa skills do projeto
* Gera arquitetura base
* Cria estrutura inicial

---

## 🧠 Estratégia de Code Understanding

* Leitura direta de arquivos
* (Opcional) Indexação por embeddings
* Mapeamento de:

  * serviços
  * entidades
  * endpoints
  * dependências

---

## 🧪 Testes e Qualidade

### ✔ Testes automatizados

* xUnit (.NET)
* JUnit (Java)
* Jest/Karma (Frontend)

### ✔ Qualidade

* Lint
* Análise estática
* Validação antes do PR

---

## 🔐 Segurança e Permissões

O agente precisa de:

* Acesso ao GitHub (clone + PR)
* Permissão para instalar pacotes
* Permissão para executar código
* Acesso a internet

### ⚠️ Importante

* Nunca expor tokens no código
* Usar variáveis de ambiente

---

## 📊 Observabilidade

* Logs por etapa
* Status de execução
* Registro de erros
* Histórico de execuções

---

## 🔄 Estratégia de Falhas

* Retry automático
* Checkpoints por etapa
* Possibilidade de rollback

---

## 🚀 Roadmap

### 🔜 Próximos passos

* [ ] Human-in-the-loop (aprovação de PR)
* [ ] Integração com CI/CD
* [ ] Memória persistente (MongoDB / Vector DB)
* [ ] RAG com documentação do projeto
* [ ] Multi-repositório
* [ ] Métricas de produtividade

---

## 🧠 Diferencial do Projeto

Este projeto não é apenas um gerador de código.

Ele é uma:

> **Fábrica de software autônoma baseada em agentes inteligentes**

Com capacidade de:

* Entender código existente
* Planejar soluções
* Implementar com contexto
* Validar automaticamente
* Entregar com qualidade

---

## ⚖️ Comparação com o mercado

| Ferramenta      | Limitação                  |
| --------------- | -------------------------- |
| GitHub Copilot  | Só sugere código           |
| Power Automate  | Low-code, limitado         |
| Copilot Studio  | Focado em chat             |
| Azure Foundry   | Execução, não orquestração |
| Agent Framework | ✔ Orquestra tudo           |

---

## 👨‍💻 Autor

**Kallebe Lins**
Especialista em IA, automação e arquitetura de sistemas

---

## 📌 Conclusão

Este projeto implementa um novo paradigma:

> **Software sendo desenvolvido por agentes, com mínima intervenção humana**

---