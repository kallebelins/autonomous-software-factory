# Tasks v1 — Autonomous Software Factory

> **Objetivo**: ao completar todas as tarefas, o projeto deve rodar localmente e ser publicável no Azure AI Foundry.

---

## Fase 0 — Pré-requisitos e Ambiente

- [ ] **0.1** Instalar .NET 8 SDK (ou superior) e validar com `dotnet --version`
- [ ] **0.2** Instalar Azure CLI e validar com `az --version`
- [ ] **0.3** Instalar extensão Azure AI Foundry na CLI: `az extension add --name ai`
- [ ] **0.4** Instalar VS Code + extensões: C# Dev Kit, Azure AI Foundry, YAML, Polyglot Notebooks
- [ ] **0.5** Instalar Git e validar com `git --version`
- [ ] **0.6** Criar conta/assinatura Azure com acesso ao Azure AI Foundry (verificar cota de modelos GPT-4o / GPT-4.1)
- [ ] **0.7** Instalar PowerShell 7+ para execução dos scripts `.ps1`
- [ ] **0.8** Clonar o repositório e navegar até a raiz do projeto

---

## Fase 1 — Provisionamento Azure AI Foundry

- [ ] **1.1** Criar Resource Group no Azure: `az group create --name rg-autonomous-factory --location eastus2`
- [ ] **1.2** Criar Azure AI Hub (workspace pai): `az ai hub create --name hub-autonomous-factory --resource-group rg-autonomous-factory --location eastus2`
- [ ] **1.3** Criar Azure AI Project dentro do Hub: `az ai project create --name proj-autonomous-factory --hub-name hub-autonomous-factory --resource-group rg-autonomous-factory`
- [ ] **1.4** Criar deployment do modelo GPT-4o (ou GPT-4.1) no projeto Foundry via portal ou CLI
- [ ] **1.5** Anotar os valores de configuração:
  - [ ] **1.5.1** Connection String do projeto (`Endpoint;ProjectName;...`)
  - [ ] **1.5.2** Model Deployment Name (ex: `gpt-4o`)
  - [ ] **1.5.3** Subscription ID, Resource Group, Project Name
- [ ] **1.6** Configurar autenticação: `az login` e validar que a identity tem role `Azure AI Developer` no projeto
- [ ] **1.7** (Opcional) Criar Azure Application Insights para tracing/telemetria
- [ ] **1.8** Documentar todos os valores no `appsettings.Development.json` (criado na Fase 2)

---

## Fase 2 — Estrutura da Solution .NET

- [ ] **2.1** Criar o arquivo de solution: `dotnet new sln -n AutonomousSoftwareFactory`
- [ ] **2.2** Criar o projeto principal: `dotnet new console -n AutonomousSoftwareFactory -o src/AutonomousSoftwareFactory`
- [ ] **2.3** Criar o projeto de testes: `dotnet new xunit -n AutonomousSoftwareFactory.Tests -o tests/AutonomousSoftwareFactory.Tests`
- [ ] **2.4** Adicionar projetos à solution:
  ```bash
  dotnet sln add src/AutonomousSoftwareFactory/AutonomousSoftwareFactory.csproj
  dotnet sln add tests/AutonomousSoftwareFactory.Tests/AutonomousSoftwareFactory.Tests.csproj
  ```
- [ ] **2.5** Adicionar referência do projeto principal nos testes:
  ```bash
  dotnet add tests/AutonomousSoftwareFactory.Tests reference src/AutonomousSoftwareFactory
  ```
- [ ] **2.6** Criar estrutura de pastas no projeto principal:
  ```
  src/AutonomousSoftwareFactory/
  ├── Configuration/
  ├── Plugins/
  ├── Helpers/
  ├── Models/
  └── Extensions/
  ```
- [ ] **2.7** Criar estrutura de pastas de testes:
  ```
  tests/AutonomousSoftwareFactory.Tests/
  ├── Plugins/
  └── Models/
  ```
- [ ] **2.8** Criar pastas de workspace:
  ```
  workspace/repos/
  workspace/artifacts/
  workspace/specs/
  workspace/temp/
  logs/
  ```
- [ ] **2.9** Criar `.gitignore` com regras para .NET, workspace/, logs/, appsettings.Development.json
- [ ] **2.10** Validar build inicial: `dotnet build` sem erros

---

## Fase 3 — Dependências NuGet

- [ ] **3.1** Adicionar `Microsoft.SemanticKernel` ao projeto principal:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Microsoft.SemanticKernel
  ```
- [ ] **3.2** Adicionar `Microsoft.SemanticKernel.Connectors.AzureOpenAI`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Microsoft.SemanticKernel.Connectors.AzureOpenAI
  ```
- [ ] **3.3** Adicionar `Azure.AI.Projects`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Azure.AI.Projects
  ```
- [ ] **3.4** Adicionar `Azure.Identity`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Azure.Identity
  ```
- [ ] **3.5** Adicionar `Microsoft.SemanticKernel.Agents.AzureAI`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Microsoft.SemanticKernel.Agents.AzureAI
  ```
- [ ] **3.6** Adicionar `YamlDotNet`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package YamlDotNet
  ```
- [ ] **3.7** Adicionar `Microsoft.Extensions.Configuration.Json`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Microsoft.Extensions.Configuration.Json
  ```
- [ ] **3.8** Adicionar `Microsoft.Extensions.Options.ConfigurationExtensions`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Microsoft.Extensions.Options.ConfigurationExtensions
  ```
- [ ] **3.9** Adicionar `Azure.Monitor.OpenTelemetry.Exporter`:
  ```bash
  dotnet add src/AutonomousSoftwareFactory package Azure.Monitor.OpenTelemetry.Exporter
  ```
- [ ] **3.10** Adicionar pacotes de teste:
  ```bash
  dotnet add tests/AutonomousSoftwareFactory.Tests package Moq
  dotnet add tests/AutonomousSoftwareFactory.Tests package FluentAssertions
  ```
- [ ] **3.11** Validar restore: `dotnet restore` sem erros
- [ ] **3.12** Validar build: `dotnet build` sem erros

> **Nota**: Os pacotes `Microsoft.Agents.Core`, `Microsoft.Agents.Manifest` e `Microsoft.Agents.Workflows` serão adicionados quando estiverem disponíveis como pacotes públicos. Até lá, usaremos o Semantic Kernel Agents + Azure AI Agents como runtime.

---

## Fase 4 — Configuração (Configuration)

- [ ] **4.1** Criar `appsettings.json` na raiz com estrutura base:
  ```json
  {
    "Foundry": {
      "ConnectionString": "",
      "ModelDeploymentName": "gpt-4o",
      "ProjectName": "",
      "ResourceGroupName": "",
      "SubscriptionId": ""
    },
    "Pipeline": {
      "WorkspacePath": "./workspace",
      "SpecsOutputPath": "./workspace/specs",
      "ReposPath": "./workspace/repos",
      "ArtifactsPath": "./workspace/artifacts",
      "LogsPath": "./logs",
      "DefaultBranch": "main"
    }
  }
  ```
- [ ] **4.2** Criar `appsettings.Development.json` com valores reais do Foundry (não commitar)
- [ ] **4.3** Criar `src/AutonomousSoftwareFactory/Configuration/FoundrySettings.cs`:
  - Propriedades: `ConnectionString`, `ModelDeploymentName`, `ProjectName`, `ResourceGroupName`, `SubscriptionId`
- [ ] **4.4** Criar `src/AutonomousSoftwareFactory/Configuration/PipelineSettings.cs`:
  - Propriedades: `WorkspacePath`, `SpecsOutputPath`, `ReposPath`, `ArtifactsPath`, `LogsPath`, `DefaultBranch`
- [ ] **4.5** Validar que o `Program.cs` carrega `appsettings.json` e `appsettings.Development.json`
- [ ] **4.6** Registrar `IOptions<FoundrySettings>` e `IOptions<PipelineSettings>` via DI
- [ ] **4.7** Validar build: `dotnet build` sem erros

---

## Fase 5 — Models (Domain DTOs)

- [ ] **5.1** Criar `src/AutonomousSoftwareFactory/Models/RequirementInput.cs`:
  - Propriedades: `Id`, `Title`, `Description`, `AcceptanceCriteria` (lista), `ProjectContext`, `TechStack`
- [ ] **5.2** Criar `src/AutonomousSoftwareFactory/Models/FeatureSpec.cs`:
  - Propriedades: `Id`, `Title`, `Description`, `Scope`, `Contracts` (inputs/outputs), `Behaviors` (lista), `EdgeCases` (lista), `AcceptanceCriteria` (lista), `Dependencies` (lista), `Status` (enum: Draft, Reviewed, Approved)
- [ ] **5.3** Criar `src/AutonomousSoftwareFactory/Models/TaskSpec.cs`:
  - Propriedades: `Id`, `FeatureId`, `Title`, `Description`, `Type` (enum: Implementation, Refactoring, Test, Config), `FilesToCreate` (lista), `FilesToModify` (lista), `Dependencies` (lista), `EstimatedComplexity` (Low/Medium/High), `Status`
- [ ] **5.4** Criar `src/AutonomousSoftwareFactory/Models/TestSpec.cs`:
  - Propriedades: `Id`, `FeatureId`, `Title`, `TestType` (Unit, Integration, E2E), `Description`, `GivenWhenThen` (lista de cenários), `ExpectedResults` (lista), `Status`
- [ ] **5.5** Criar enums auxiliares: `SpecStatus`, `TaskType`, `TestType`, `Complexity`
- [ ] **5.6** Validar build: `dotnet build` sem erros
- [ ] **5.7** Criar testes unitários para serialização/desserialização YAML dos models

---

## Fase 6 — Helpers

- [ ] **6.1** Criar `src/AutonomousSoftwareFactory/Helpers/ProcessHelper.cs`:
  - Método `RunAsync(string command, string args, string workingDir)` → retorna `(int exitCode, string stdout, string stderr)`
  - Usar `System.Diagnostics.Process` com redirect de stdout/stderr
  - Timeout configurável (default 5 min)
  - Logging de comando executado
- [ ] **6.2** Criar testes unitários para `ProcessHelper` (testar com comando simples como `dotnet --version`)
- [ ] **6.3** Validar build e testes: `dotnet test`

---

## Fase 7 — Plugins (Semantic Kernel KernelFunction)

### 7.1 — FileOperationsPlugin
- [ ] **7.1.1** Criar `src/AutonomousSoftwareFactory/Plugins/FileOperationsPlugin.cs`
- [ ] **7.1.2** Implementar `[KernelFunction] ReadFile(string path)` → retorna conteúdo do arquivo
- [ ] **7.1.3** Implementar `[KernelFunction] WriteFile(string path, string content)` → escreve arquivo
- [ ] **7.1.4** Implementar `[KernelFunction] ListFiles(string directory, string pattern)` → lista arquivos
- [ ] **7.1.5** Implementar `[KernelFunction] FileExists(string path)` → retorna bool
- [ ] **7.1.6** Implementar `[KernelFunction] CreateDirectory(string path)` → cria diretório
- [ ] **7.1.7** Adicionar validação de segurança (paths dentro do workspace apenas)
- [ ] **7.1.8** Criar testes unitários

### 7.2 — GitOperationsPlugin
- [ ] **7.2.1** Criar `src/AutonomousSoftwareFactory/Plugins/GitOperationsPlugin.cs`
- [ ] **7.2.2** Implementar `[KernelFunction] CloneRepository(string repoUrl, string targetDir)` via `ProcessHelper`
- [ ] **7.2.3** Implementar `[KernelFunction] CreateBranch(string repoDir, string branchName)`
- [ ] **7.2.4** Implementar `[KernelFunction] CommitChanges(string repoDir, string message)`
- [ ] **7.2.5** Implementar `[KernelFunction] PushBranch(string repoDir, string branchName)`
- [ ] **7.2.6** Implementar `[KernelFunction] GetStatus(string repoDir)` → retorna git status
- [ ] **7.2.7** Implementar `[KernelFunction] GetDiff(string repoDir)` → retorna git diff
- [ ] **7.2.8** Criar testes unitários (mock de ProcessHelper)

### 7.3 — SpecOperationsPlugin
- [ ] **7.3.1** Criar `src/AutonomousSoftwareFactory/Plugins/SpecOperationsPlugin.cs`
- [ ] **7.3.2** Implementar `[KernelFunction] CreateFeatureSpec(string specYaml, string runId)` → salva spec YAML em `workspace/specs/{runId}/features/`
- [ ] **7.3.3** Implementar `[KernelFunction] CreateTaskSpec(string specYaml, string runId)` → salva em `workspace/specs/{runId}/tasks/`
- [ ] **7.3.4** Implementar `[KernelFunction] CreateTestSpec(string specYaml, string runId)` → salva em `workspace/specs/{runId}/tests/`
- [ ] **7.3.5** Implementar `[KernelFunction] ReadSpec(string specPath)` → lê e retorna conteúdo da spec
- [ ] **7.3.6** Implementar `[KernelFunction] ListSpecs(string runId, string specType)` → lista specs por tipo
- [ ] **7.3.7** Implementar `[KernelFunction] ValidateSpec(string specYaml, string schemaType)` → valida YAML contra schema
- [ ] **7.3.8** Criar testes unitários com specs de exemplo

### 7.4 — BuildProjectPlugin
- [ ] **7.4.1** Criar `src/AutonomousSoftwareFactory/Plugins/BuildProjectPlugin.cs`
- [ ] **7.4.2** Implementar `[KernelFunction] BuildDotNet(string projectDir)` → `dotnet build`
- [ ] **7.4.3** Implementar `[KernelFunction] BuildNpm(string projectDir)` → `npm run build`
- [ ] **7.4.4** Implementar `[KernelFunction] RestoreDependencies(string projectDir, string stack)` → restore por stack
- [ ] **7.4.5** Criar testes unitários

### 7.5 — RunTestsPlugin
- [ ] **7.5.1** Criar `src/AutonomousSoftwareFactory/Plugins/RunTestsPlugin.cs`
- [ ] **7.5.2** Implementar `[KernelFunction] RunDotNetTests(string projectDir)` → `dotnet test`
- [ ] **7.5.3** Implementar `[KernelFunction] RunPytestTests(string projectDir)` → `pytest`
- [ ] **7.5.4** Implementar `[KernelFunction] RunNpmTests(string projectDir)` → `npm test`
- [ ] **7.5.5** Criar testes unitários

### 7.6 — CreatePullRequestPlugin
- [ ] **7.6.1** Criar `src/AutonomousSoftwareFactory/Plugins/CreatePullRequestPlugin.cs`
- [ ] **7.6.2** Implementar `[KernelFunction] CreateGitHubPR(string repoDir, string title, string description, string sourceBranch, string targetBranch)` via `gh` CLI
- [ ] **7.6.3** Implementar `[KernelFunction] CreateAzureDevOpsPR(...)` via `az repos pr create`
- [ ] **7.6.4** Criar testes unitários

### 7.7 — StackDetectorPlugin
- [ ] **7.7.1** Criar `src/AutonomousSoftwareFactory/Plugins/StackDetectorPlugin.cs`
- [ ] **7.7.2** Implementar `[KernelFunction] DetectStack(string projectDir)` → analisa arquivos do projeto (`.csproj`, `package.json`, `requirements.txt`, `pom.xml`, etc.) e retorna stack detectada
- [ ] **7.7.3** Criar testes unitários

- [ ] **7.8** Validar build completo: `dotnet build` sem erros
- [ ] **7.9** Validar todos os testes: `dotnet test` passando

---

## Fase 8 — Extensions (Bootstrap e Registro)

- [ ] **8.1** Criar `src/AutonomousSoftwareFactory/Extensions/ServiceCollectionExtensions.cs`
- [ ] **8.2** Implementar método `AddAutonomousFactoryPlugins(this IKernelBuilder builder)`:
  - Registra todos os plugins via `builder.Plugins.AddFromType<T>()`
  - Plugins: `FileOperationsPlugin`, `GitOperationsPlugin`, `SpecOperationsPlugin`, `BuildProjectPlugin`, `RunTestsPlugin`, `CreatePullRequestPlugin`, `StackDetectorPlugin`
- [ ] **8.3** Implementar método `AddFoundryAgent(this IServiceCollection services, IConfiguration config)`:
  - Lê `FoundrySettings` da configuration
  - Cria `AIProjectClient` com `DefaultAzureCredential`
  - Registra como singleton
- [ ] **8.4** Implementar método `AddOpenTelemetryTracing(this IServiceCollection services, IConfiguration config)`:
  - Configura `Azure.Monitor.OpenTelemetry.Exporter` se Application Insights estiver configurado
- [ ] **8.5** Validar build: `dotnet build` sem erros

---

## Fase 9 — Prompts (.prompty files)

- [ ] **9.1** Criar `prompts/requirements-analyst.prompty`:
  - System prompt para análise de requisitos, decomposição em features
  - Input: requisito bruto do usuário
  - Output: lista de features identificadas com escopo
- [ ] **9.2** Criar `prompts/planner.prompty`:
  - System prompt para planejamento técnico
  - Input: lista de features
  - Output: plano de execução com dependências
- [ ] **9.3** Criar `prompts/spec-writer.prompty`:
  - System prompt para escrita de specs SDD
  - Input: feature + contexto técnico
  - Output: FeatureSpec completa em YAML
  - Incluir instruções detalhadas sobre formato, campos obrigatórios, edge cases
- [ ] **9.4** Criar `prompts/spec-reviewer.prompty`:
  - System prompt para revisão de specs
  - Input: spec YAML gerada
  - Output: aprovação ou lista de ajustes necessários
  - Critérios: completude, clareza, testabilidade, consistência
- [ ] **9.5** Criar `prompts/architect.prompty`:
  - System prompt para decisões de arquitetura
  - Input: specs aprovadas + stack detectada
  - Output: decisões de design (patterns, estrutura de pastas, interfaces)
- [ ] **9.6** Criar `prompts/developer.prompty`:
  - System prompt para geração de código
  - Input: TaskSpec + FeatureSpec + decisões de arquitetura
  - Output: código-fonte completo por arquivo
  - Instruções: seguir spec ao pé da letra, usar patterns definidos pelo arquiteto
- [ ] **9.7** Criar `prompts/code-reviewer.prompty`:
  - System prompt para revisão de código
  - Input: código gerado + spec original
  - Output: aprovação ou lista de issues (bugs, desvios da spec, code smells)
- [ ] **9.8** Criar `prompts/tester.prompty`:
  - System prompt para geração de testes
  - Input: TestSpec + código gerado
  - Output: código de testes (unit, integration)
- [ ] **9.9** Criar `prompts/devops.prompty`:
  - System prompt para CI/CD e deploy
  - Input: artefatos de build + configuração de deploy
  - Output: pipeline YAML, Dockerfile, scripts de deploy

---

## Fase 10 — Manifests de Agents (YAML Declarativo)

- [ ] **10.1** Criar `manifests/agents/requirements-analyst.agent.yaml`:
  ```yaml
  name: requirements-analyst
  description: Analisa requisitos e decompõe em features
  instructions: file:../../prompts/requirements-analyst.prompty
  model: ${env:MODEL_DEPLOYMENT_NAME}
  tools:
    - file-operations
    - spec-operations
  ```
- [ ] **10.2** Criar `manifests/agents/planner.agent.yaml` com referência ao prompt e tools
- [ ] **10.3** Criar `manifests/agents/spec-writer.agent.yaml` com tools: `spec-operations`, `file-operations`
- [ ] **10.4** Criar `manifests/agents/spec-reviewer.agent.yaml` com tools: `spec-operations`
- [ ] **10.5** Criar `manifests/agents/architect.agent.yaml` com tools: `file-operations`, `stack-detector`
- [ ] **10.6** Criar `manifests/agents/developer.agent.yaml` com tools: `file-operations`, `spec-operations`, `build-project`
- [ ] **10.7** Criar `manifests/agents/code-reviewer.agent.yaml` com tools: `file-operations`, `spec-operations`
- [ ] **10.8** Criar `manifests/agents/tester.agent.yaml` com tools: `file-operations`, `run-tests`, `spec-operations`
- [ ] **10.9** Criar `manifests/agents/devops.agent.yaml` com tools: `git-operations`, `build-project`, `create-pull-request`
- [ ] **10.10** Validar que todos os YAML são parseable (teste com YamlDotNet)

---

## Fase 11 — Tool Manifests (YAML)

- [ ] **11.1** Criar `manifests/tools/git-operations.tool.yaml`:
  - Declarar cada function do `GitOperationsPlugin` com nome, descrição e parâmetros
- [ ] **11.2** Criar `manifests/tools/build-project.tool.yaml`
- [ ] **11.3** Criar `manifests/tools/run-tests.tool.yaml`
- [ ] **11.4** Criar `manifests/tools/create-pull-request.tool.yaml`
- [ ] **11.5** Criar `manifests/tools/file-operations.tool.yaml`
- [ ] **11.6** Criar `manifests/tools/spec-operations.tool.yaml`
- [ ] **11.7** Validar que tool names nos agent YAML correspondem aos tool manifest names

---

## Fase 12 — Spec Schemas (Validação SDD)

- [ ] **12.1** Criar `manifests/specs/feature-spec.schema.yaml`:
  - Definir campos obrigatórios: `id`, `title`, `description`, `scope`, `contracts`, `behaviors`, `acceptance_criteria`
  - Definir tipos de cada campo
- [ ] **12.2** Criar `manifests/specs/task-spec.schema.yaml`:
  - Campos: `id`, `feature_id`, `title`, `type`, `files_to_create`, `files_to_modify`
- [ ] **12.3** Criar `manifests/specs/test-spec.schema.yaml`:
  - Campos: `id`, `feature_id`, `test_type`, `scenarios` (given/when/then)
- [ ] **12.4** Criar `manifests/specs/api-spec.schema.yaml`:
  - Campos para contratos de API (endpoints, request/response schemas)
- [ ] **12.5** Criar samples em `samples/specs/`:
  - [ ] **12.5.1** `feature-spec-sample.yaml` com exemplo completo
  - [ ] **12.5.2** `task-spec-sample.yaml` com exemplo completo

---

## Fase 13 — Workflow Manifests (YAML Declarativo)

- [ ] **13.1** Criar `manifests/workflows/full-pipeline.workflow.yaml`:
  ```yaml
  name: full-pipeline
  description: Pipeline completo da fábrica de software
  steps:
    - name: analyze-requirements
      agent: requirements-analyst
      input: ${inputs.requirement}
      output: features

    - name: plan-execution
      agent: planner
      input: ${steps.analyze-requirements.output}
      output: execution-plan

    - name: write-specs
      agent: spec-writer
      input: ${steps.plan-execution.output}
      output: specs
      loop: ${steps.analyze-requirements.output.features}

    - name: review-specs
      agent: spec-reviewer
      input: ${steps.write-specs.output}
      output: reviewed-specs

    - name: design-architecture
      agent: architect
      input: ${steps.review-specs.output}
      output: architecture

    - name: generate-code
      agent: developer
      input:
        specs: ${steps.review-specs.output}
        architecture: ${steps.design-architecture.output}
      output: code
      loop: ${steps.review-specs.output.tasks}

    - name: review-code
      agent: code-reviewer
      input: ${steps.generate-code.output}
      output: review-result

    - name: generate-tests
      agent: tester
      input:
        specs: ${steps.review-specs.output}
        code: ${steps.generate-code.output}
      output: tests

    - name: deploy
      agent: devops
      input: ${steps.generate-tests.output}
      output: deployment-result
  ```
- [ ] **13.2** Criar `manifests/workflows/spec-only.workflow.yaml`:
  - Steps: analyze-requirements → plan → write-specs → review-specs
  - Útil para validar o fluxo SDD isoladamente
- [ ] **13.3** Criar `manifests/workflows/code-review-only.workflow.yaml`:
  - Steps: read existing code → review → generate report
- [ ] **13.4** Criar `manifests/workflows/test-and-deploy.workflow.yaml`:
  - Steps: read specs → generate tests → build → run tests → create PR

---

## Fase 14 — Program.cs (Bootstrap Principal)

- [ ] **14.1** Implementar `Program.cs` com o fluxo completo:
  - [ ] **14.1.1** Configurar `IConfiguration` (appsettings.json + Development + env vars)
  - [ ] **14.1.2** Criar `IServiceCollection` e registrar settings (`IOptions<FoundrySettings>`, `IOptions<PipelineSettings>`)
  - [ ] **14.1.3** Criar `Kernel` via `KernelBuilder`
  - [ ] **14.1.4** Adicionar Azure OpenAI chat completion ao Kernel (via Foundry connection)
  - [ ] **14.1.5** Registrar todos os plugins no Kernel (`AddFromType<T>()`)
  - [ ] **14.1.6** Criar `AIProjectClient` com `DefaultAzureCredential`
  - [ ] **14.1.7** Criar `AzureAIAgent` para cada agente (loop nos manifests YAML ou criação programática)
  - [ ] **14.1.8** Criar `AgentThread` para a sessão
  - [ ] **14.1.9** Implementar o pipeline sequencial (invocar cada agente na ordem do workflow)
  - [ ] **14.1.10** Gravar outputs em `workspace/specs/{runId}/`
  - [ ] **14.1.11** Logging de cada step (início, fim, resultado)
- [ ] **14.2** Aceitar argumento de linha de comando para:
  - `--workflow` (qual workflow executar: `full-pipeline`, `spec-only`, etc.)
  - `--requirement` (caminho do arquivo JSON de requisito)
  - `--run-id` (identificador da execução, default: GUID)
- [ ] **14.3** Implementar tratamento de erros global com logging
- [ ] **14.4** Validar build: `dotnet build` sem erros

---

## Fase 15 — Samples

- [ ] **15.1** Criar `samples/requirement-sample.json`:
  ```json
  {
    "id": "REQ-001",
    "title": "API de Gerenciamento de Tarefas",
    "description": "Criar uma API REST para gerenciamento de tarefas (CRUD) com autenticação JWT, seguindo Clean Architecture em .NET 8",
    "acceptanceCriteria": [
      "Endpoints CRUD para tarefas",
      "Autenticação via JWT",
      "Validação de input",
      "Testes unitários com cobertura > 80%",
      "Swagger/OpenAPI documentation"
    ],
    "projectContext": "Novo projeto greenfield",
    "techStack": "dotnet"
  }
  ```
- [ ] **15.2** Criar `samples/project-metadata-sample.json` com metadados do projeto alvo
- [ ] **15.3** Validar que os samples são parseáveis pelos models

---

## Fase 16 — Scripts de Automação

- [ ] **16.1** Criar `scripts/setup-env.ps1`:
  - Verificar pré-requisitos (dotnet, az, git)
  - Criar pastas do workspace
  - Restaurar pacotes NuGet
  - Validar configuração do appsettings
- [ ] **16.2** Criar `scripts/run-local.ps1`:
  - Aceitar parâmetros: `-Workflow`, `-RequirementFile`, `-RunId`
  - Executar `dotnet run --project src/AutonomousSoftwareFactory` com os parâmetros
  - Exibir logs em tempo real
  - Ao final, exibir caminho dos artefatos gerados
- [ ] **16.3** Criar `scripts/provision-foundry.ps1`:
  - Criar Resource Group
  - Criar AI Hub
  - Criar AI Project
  - Criar model deployment
  - Exibir connection string e instruções para `appsettings.Development.json`

---

## Fase 17 — Documentação

- [ ] **17.1** Criar/atualizar `README.md`:
  - Descrição do projeto
  - Pré-requisitos
  - Quick start (3 passos: provisionar, configurar, executar)
  - Arquitetura overview
  - Link para docs detalhados
- [ ] **17.2** Criar `docs/architecture-overview.md`:
  - Diagrama de fluxo do pipeline
  - Descrição de cada agente e seu papel
  - Descrição dos plugins e capabilities
- [ ] **17.3** Criar `docs/steps-and-resources.md`:
  - Detalhamento de cada step do workflow
  - Recursos consumidos (modelos, tokens estimados)
- [ ] **17.4** Criar `docs/spec-driven-development.md`:
  - Explicação do SDD no contexto do projeto
  - Exemplos de specs
  - Fluxo spec → código → teste

---

## Fase 18 — Testes End-to-End

- [ ] **18.1** Criar teste de integração: `FileOperationsPlugin` lê/escreve arquivo real no workspace/temp
- [ ] **18.2** Criar teste de integração: `SpecOperationsPlugin` gera spec YAML e valida contra schema
- [ ] **18.3** Criar teste de integração: `ProcessHelper` executa `dotnet --version` com sucesso
- [ ] **18.4** Criar teste de smoke: conectar ao Foundry e verificar que o modelo responde (requer Azure configurado)
- [ ] **18.5** Executar `dotnet test` — todos os testes devem passar
- [ ] **18.6** Executar `scripts/run-local.ps1 -Workflow spec-only -RequirementFile samples/requirement-sample.json`:
  - Validar que specs são geradas em `workspace/specs/{runId}/features/`
  - Validar que o pipeline completa sem erros

---

## Fase 19 — Execução Local Completa

- [ ] **19.1** Executar `scripts/setup-env.ps1` — sem erros
- [ ] **19.2** Executar pipeline `spec-only` com o sample de requisito
- [ ] **19.3** Revisar specs geradas — devem ter formato YAML válido e conteúdo coerente
- [ ] **19.4** Executar pipeline `full-pipeline` com o sample de requisito
- [ ] **19.5** Verificar que código foi gerado em `workspace/repos/`
- [ ] **19.6** Verificar que testes foram gerados
- [ ] **19.7** Verificar logs em `logs/`
- [ ] **19.8** Corrigir quaisquer erros encontrados e re-executar

---

## Fase 20 — Publicação no Azure AI Foundry

- [ ] **20.1** Validar que o projeto Foundry está ativo: `az ai project show --name proj-autonomous-factory`
- [ ] **20.2** Registrar cada agente como Agent no Foundry:
  ```csharp
  var agent = await AzureAIAgent.CreateAsync(
      clientProvider,
      modelId,
      name: "requirements-analyst",
      instructions: File.ReadAllText("prompts/requirements-analyst.prompty"),
      tools: [kernelFunctionTools]
  );
  ```
- [ ] **20.3** Verificar que os agentes aparecem no portal do AI Foundry
- [ ] **20.4** Testar invocação de um agente individual via portal do Foundry
- [ ] **20.5** Testar pipeline completo apontando para agentes no Foundry
- [ ] **20.6** Configurar Application Insights e validar que traces aparecem no portal
- [ ] **20.7** (Opcional) Criar Managed Identity para o App Service/Container que executará o pipeline em produção
- [ ] **20.8** (Opcional) Configurar CI/CD (GitHub Actions ou Azure DevOps) para:
  - Build automático
  - Execução de testes
  - Deploy dos agentes no Foundry

---

## Resumo de Progresso

| Fase | Descrição | Tasks | Concluídas |
|------|-----------|-------|------------|
| 0 | Pré-requisitos | 8 | 0 |
| 1 | Provisionamento Azure | 8 | 0 |
| 2 | Estrutura Solution | 10 | 0 |
| 3 | Dependências NuGet | 12 | 0 |
| 4 | Configuration | 7 | 0 |
| 5 | Models | 7 | 0 |
| 6 | Helpers | 3 | 0 |
| 7 | Plugins | ~35 | 0 |
| 8 | Extensions | 5 | 0 |
| 9 | Prompts | 9 | 0 |
| 10 | Agent Manifests | 10 | 0 |
| 11 | Tool Manifests | 7 | 0 |
| 12 | Spec Schemas | 5 | 0 |
| 13 | Workflow Manifests | 4 | 0 |
| 14 | Program.cs | 4 | 0 |
| 15 | Samples | 3 | 0 |
| 16 | Scripts | 3 | 0 |
| 17 | Documentação | 4 | 0 |
| 18 | Testes E2E | 6 | 0 |
| 19 | Execução Local | 8 | 0 |
| 20 | Publicação Foundry | 8 | 0 |
| **Total** | | **~166** | **0** |
