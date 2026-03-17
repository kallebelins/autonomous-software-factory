# Estrutura do Projeto — Autonomous Software Factory (Microsoft Agent Framework)

## Objetivo

Solução em .NET que utiliza **integralmente o Microsoft Agent Framework (MAF)** para:

* definir agentes declarativos (manifesto YAML)
* definir workflows declarativos (orquestração de agentes)
* registrar tools/functions customizadas como **Semantic Kernel Plugins** (`KernelFunction`)
* integrar nativamente com **Azure AI Foundry** (conexão, modelos, tracing)
* executar o pipeline completo de fábrica de software autônoma
* **produzir artefatos de especificação (specs) por funcionalidade** seguindo o padrão **Spec-Driven Development (SDD)**

### Referências

* [Declarative Workflows — Agent Framework](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows?pivots=programming-language-csharp)
* [Declarative Agents — Agent Framework](https://learn.microsoft.com/en-us/agent-framework/concepts/agents?pivots=programming-language-csharp)
* [Agent Tools — Agent Framework](https://learn.microsoft.com/en-us/agent-framework/concepts/tools?pivots=programming-language-csharp)
* [Function Tools — Agent Framework](https://learn.microsoft.com/en-us/agent-framework/concepts/tools/function-tools?pivots=programming-language-csharp)
* [VS Code Agents Workflow (low-code) — Azure AI Foundry](https://learn.microsoft.com/en-us/azure/ai-foundry/agents/how-to/vs-code-agents-workflow-low-code)
* [Spec-Driven Development (SDD) — Initial Review](https://dev.to/danielsogl/spec-driven-development-sdd-a-initial-review-2llp)
* [SDD Tools — Martin Fowler](https://martinfowler.com/articles/exploring-gen-ai/sdd-3-tools.html)

---

## Spec-Driven Development (SDD) — Visão Geral

O SDD é um padrão onde **especificações formais são o artefato central do pipeline**, não o código. Cada funcionalidade passa por:

1. **Decomposição** — o requisito é quebrado em features independentes
2. **Especificação** — cada feature recebe uma spec detalhada (contrato, comportamento, edge cases, critérios de aceite)
3. **Planejamento** — a spec é decomposta em tarefas técnicas granulares (task specs)
4. **Validação** — specs são validadas antes da geração de código
5. **Geração** — código é gerado **a partir da spec**, não do requisito bruto
6. **Verificação** — testes são gerados **a partir da spec**, garantindo rastreabilidade

### Benefícios no contexto da fábrica

* **Rastreabilidade**: cada linha de código gerada aponta para uma spec
* **Verificabilidade**: specs podem ser validadas por schema antes do LLM gerar código
* **Reprodutibilidade**: specs são artefatos determinísticos; mesma spec → mesmo código esperado
* **Revisão humana**: specs são legíveis e podem ser aprovadas antes da geração
* **Iteração**: se o código gerado falha, a spec é o ponto de ajuste — não o prompt

---

## Decisões de arquitetura — Integração nativa com MAF / SK / Foundry

### O que já existe no framework (usamos nativamente, sem reimplementar)

| Conceito | Fornecido por | API nativa |
|---|---|---|
| Workflow engine / executor | MAF Declarative Workflows | `WorkflowRuntime`, YAML workflow manifests |
| Workflow step definition | MAF Declarative Workflows | Steps definidos no YAML do workflow |
| Workflow result / output | MAF Declarative Workflows | Output bindings no YAML |
| Activity (invoke agent step) | MAF Declarative Workflows | `agent-invoke` step type no YAML |
| Pipeline state management | MAF `AgentThread` / SK `ChatHistory` | `AgentThread`, thread context |
| Step output / inter-step data | MAF workflow variables | `variables` e `outputs` no YAML |
| Agent registration / factory | MAF `AgentManifest` + `AgentFactory` | `AgentFactory.CreateFromManifestAsync()` |
| Agent invocation | MAF `Agent.InvokeAsync()` | Channel nativo do Foundry |
| Tracing / observability | Azure AI Foundry + OpenTelemetry | Integração nativa via `AddAzureAIFoundry()` |

### O que implementamos (extensões legítimas)

| Conceito | Motivo | Padrão SK/MAF |
|---|---|---|
| **Plugins (Tools)** | Lógica de domínio (git, build, specs, files) | `KernelFunction` + `KernelPlugin` |
| **Configuration** | Settings específicos do projeto | `IOptions<T>` padrão .NET |
| **Domain Models** | DTOs de spec (FeatureSpec, TaskSpec, TestSpec) | POCOs simples |
| **Helpers** | Utilitários de infra (process execution) | Classes auxiliares |
| **Plugin Registration** | Registro de plugins no Kernel | `kernel.Plugins.AddFromType<T>()` |
| **Foundry Bootstrap** | Setup de conexão com AI Foundry | `AzureAIAgent.CreateAsync()` |

---

## Estrutura do projeto

```text
autonomous-software-factory/
│
├── README.md
├── project-structure.md
├── .gitignore
├── AutonomousSoftwareFactory.sln
├── appsettings.json
├── appsettings.Development.json
│
├── manifests/
│   │
│   ├── agents/                              # Agentes declarativos (MAF AgentManifest YAML)
│   │   ├── requirements-analyst.agent.yaml  #   → carregados via AgentFactory.CreateFromManifestAsync()
│   │   ├── planner.agent.yaml
│   │   ├── spec-writer.agent.yaml
│   │   ├── spec-reviewer.agent.yaml
│   │   ├── architect.agent.yaml
│   │   ├── developer.agent.yaml
│   │   ├── code-reviewer.agent.yaml
│   │   ├── tester.agent.yaml
│   │   └── devops.agent.yaml
│   │
│   ├── workflows/                           # Workflows declarativos (MAF WorkflowRuntime YAML)
│   │   ├── full-pipeline.workflow.yaml      #   → executados via WorkflowRuntime nativo
│   │   ├── spec-only.workflow.yaml          #   → steps usam agent-invoke, não Activities C#
│   │   ├── code-review-only.workflow.yaml
│   │   └── test-and-deploy.workflow.yaml
│   │
│   ├── tools/                               # Tool manifests (referenciados pelos agents YAML)
│   │   ├── git-operations.tool.yaml
│   │   ├── build-project.tool.yaml
│   │   ├── run-tests.tool.yaml
│   │   ├── create-pull-request.tool.yaml
│   │   ├── file-operations.tool.yaml
│   │   └── spec-operations.tool.yaml
│   │
│   └── specs/                               # Schemas de validação de specs (SDD)
│       ├── feature-spec.schema.yaml
│       ├── task-spec.schema.yaml
│       ├── test-spec.schema.yaml
│       └── api-spec.schema.yaml
│
├── prompts/                                 # Prompt templates (.prompty) referenciados nos agents
│   ├── requirements-analyst.prompty
│   ├── planner.prompty
│   ├── spec-writer.prompty
│   ├── spec-reviewer.prompty
│   ├── architect.prompty
│   ├── developer.prompty
│   ├── code-reviewer.prompty
│   ├── tester.prompty
│   └── devops.prompty
│
├── samples/
│   ├── requirement-sample.json
│   ├── project-metadata-sample.json
│   └── specs/
│       ├── feature-spec-sample.yaml
│       └── task-spec-sample.yaml
│
├── docs/
│   ├── architecture-overview.md
│   ├── steps-and-resources.md
│   └── spec-driven-development.md
│
├── src/
│   └── AutonomousSoftwareFactory/
│       ├── Program.cs                       # Bootstrap: Kernel + Plugins + Foundry + WorkflowRuntime
│       ├── AutonomousSoftwareFactory.csproj
│       │
│       ├── Configuration/
│       │   ├── FoundrySettings.cs           # Connection string, project, model deployments
│       │   └── PipelineSettings.cs          # Workspace paths, run config
│       │
│       ├── Plugins/                         # SK KernelFunction Plugins (tools reais)
│       │   ├── GitOperationsPlugin.cs       #   → [KernelFunction] git clone/commit/push
│       │   ├── BuildProjectPlugin.cs        #   → [KernelFunction] dotnet build / npm build
│       │   ├── RunTestsPlugin.cs            #   → [KernelFunction] dotnet test / pytest
│       │   ├── CreatePullRequestPlugin.cs   #   → [KernelFunction] GitHub/Azure DevOps PR
│       │   ├── FileOperationsPlugin.cs      #   → [KernelFunction] read/write/list files
│       │   ├── SpecOperationsPlugin.cs      #   → [KernelFunction] create/validate/read specs
│       │   └── StackDetectorPlugin.cs       #   → [KernelFunction] detect project stack
│       │
│       ├── Helpers/
│       │   └── ProcessHelper.cs             # Execução de processos externos (CLI)
│       │
│       ├── Models/                          # DTOs de domínio
│       │   ├── FeatureSpec.cs               #   → Modelo da spec de feature (SDD)
│       │   ├── TaskSpec.cs                  #   → Modelo da spec de task (SDD)
│       │   ├── TestSpec.cs                  #   → Modelo da spec de teste (SDD)
│       │   └── RequirementInput.cs          #   → Input do usuário (requisito bruto)
│       │
│       └── Extensions/
│           └── ServiceCollectionExtensions.cs  # Registro de Plugins no Kernel
│                                               # Setup AzureAIAgent via Foundry
│                                               # Configuração de tracing/OpenTelemetry
│
├── tests/
│   └── AutonomousSoftwareFactory.Tests/
│       ├── AutonomousSoftwareFactory.Tests.csproj
│       ├── Plugins/
│       │   ├── GitOperationsPluginTests.cs
│       │   ├── SpecOperationsPluginTests.cs
│       │   └── StackDetectorPluginTests.cs
│       └── Models/
│           ├── FeatureSpecTests.cs
│           └── TaskSpecTests.cs
│
├── workspace/
│   ├── repos/                               # Repositórios clonados para geração de código
│   ├── artifacts/                           # Artefatos de build e deploy
│   ├── specs/                               # Specs geradas por execução
│   │   └── {run-id}/
│   │       ├── features/
│   │       │   ├── feature-001.spec.yaml
│   │       │   └── feature-002.spec.yaml
│   │       ├── tasks/
│   │       │   ├── feature-001-task-01.spec.yaml
│   │       │   └── feature-001-task-02.spec.yaml
│   │       └── tests/
│   │           ├── feature-001.test-spec.yaml
│   │           └── feature-002.test-spec.yaml
│   └── temp/                                # Arquivos temporários de execução
│
├── logs/                                    # Logs de execução do pipeline
│
└── scripts/
    ├── setup-env.ps1                        # Configuração do ambiente local
    ├── run-local.ps1                        # Execução local do pipeline
    └── provision-foundry.ps1                # Provisionamento de recursos no Azure AI Foundry
```

---

## Fluxo de execução (100% nativo MAF)

```text
Program.cs
  │
  ├── 1. Build Kernel (SK)
  │     └── kernel.Plugins.AddFromType<GitOperationsPlugin>()
  │     └── kernel.Plugins.AddFromType<SpecOperationsPlugin>()
  │     └── kernel.Plugins.AddFromType<FileOperationsPlugin>()
  │     └── ...
  │
  ├── 2. Connect to Azure AI Foundry
  │     └── AzureAIAgent.CreateAsync(clientProvider, modelId, ...)
  │
  ├── 3. Load Agents from Manifests
  │     └── AgentFactory.CreateFromManifestAsync("manifests/agents/*.yaml")
  │       → Cada agent YAML referencia:
  │           • prompt template (.prompty)
  │           • tools (tool.yaml → KernelFunction plugins)
  │           • model deployment (via Foundry)
  │
  ├── 4. Load & Execute Workflow
  │     └── WorkflowRuntime.RunAsync("manifests/workflows/full-pipeline.workflow.yaml")
  │       → O runtime do MAF:
  │           • lê o YAML do workflow
  │           • resolve agents por nome
  │           • executa steps sequenciais/paralelos
  │           • passa variáveis entre steps
  │           • invoca tools (plugins) conforme o agente decide
  │           • gerencia AgentThread (histórico de conversa)
  │
  └── 5. Outputs
        └── Specs geradas em workspace/specs/{run-id}/
        └── Código gerado em workspace/repos/
        └── Artefatos em workspace/artifacts/
        └── Logs em logs/
```

---

## Pacotes NuGet necessários

```xml
<!-- Core -->
<PackageReference Include="Microsoft.SemanticKernel" />
<PackageReference Include="Microsoft.Agents.Core" />
<PackageReference Include="Microsoft.Agents.Manifest" />

<!-- Azure AI Foundry -->
<PackageReference Include="Microsoft.SemanticKernel.Connectors.AzureAIFoundry" />
<PackageReference Include="Azure.AI.Projects" />

<!-- Workflows (quando disponível como pacote separado) -->
<PackageReference Include="Microsoft.Agents.Workflows" />

<!-- Tracing -->
<PackageReference Include="Azure.Monitor.OpenTelemetry.Exporter" />

<!-- Helpers -->
<PackageReference Include="YamlDotNet" />
```
