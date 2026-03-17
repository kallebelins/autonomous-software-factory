# Estrutura do Projeto — Autonomous Software Factory

## Objetivo

Solução simples em .NET que:

* lê workflows declarativos em YAML
* carrega agentes, skills e tools
* executa o pipeline completo
* integra com GitHub

---

## Estrutura do projeto

```text
autonomous-software-factory/
│
├── README.md
├── .gitignore
├── AutonomousSoftwareFactory.sln
├── appsettings.json
│
├── configs/
│   ├── workflow.yaml
│   ├── agents.yaml
│   ├── skills_registry.yaml
│   ├── tools.yaml
│   └── prompts.yaml
│
├── samples/
│   ├── requirement-sample.json
│   └── project-metadata-sample.json
│
├── docs/
│   ├── comparison.md
│   └── steps-and-resources.md
│
├── src/
│   └── AutonomousSoftwareFactory/
│       ├── Program.cs
│       ├── AutonomousSoftwareFactory.csproj
│       │
│       ├── Models/
│       │   ├── WorkflowDefinition.cs
│       │   ├── StepDefinition.cs
│       │   ├── AgentDefinition.cs
│       │   ├── SkillDefinition.cs
│       │   ├── ToolDefinition.cs
│       │   ├── ExecutionContext.cs
│       │   └── ExecutionResult.cs
│       │
│       ├── Yaml/
│       │   └── YamlConfigLoader.cs
│       │
│       ├── Workflow/
│       │   ├── IWorkflowEngine.cs
│       │   └── WorkflowEngine.cs
│       │
│       ├── Agents/
│       │   ├── IAgentExecutor.cs
│       │   └── AgentExecutor.cs
│       │
│       ├── Tools/
│       │   ├── IToolExecutor.cs
│       │   └── ToolExecutor.cs
│       │
│       ├── Llm/
│       │   ├── ILlmClient.cs
│       │   └── LlmClient.cs
│       │
│       └── State/
│           ├── IStateStore.cs
│           └── InMemoryStateStore.cs
│
├── tests/
│   └── AutonomousSoftwareFactory.Tests/
│       └── AutonomousSoftwareFactory.Tests.csproj
│
├── workspace/
│   ├── repos/
│   ├── artifacts/
│   └── temp/
│
├── logs/
│
└── scripts/
    ├── setup-env.ps1
    └── run-local.ps1
```

---

# Papel de cada pasta

## `configs/`

Núcleo declarativo da solução.

* `workflow.yaml` — sequência completa do fluxo (steps, inputs, outputs, políticas)
* `agents.yaml` — definição dos agentes (nome, skills, tools, prompt)
* `skills_registry.yaml` — capacidades cognitivas e operacionais
* `tools.yaml` — catálogo de ferramentas executáveis (comandos, APIs)
* `prompts.yaml` — prompts centralizados por agente e contexto

---

## `samples/`

Arquivos de entrada para execução local sem dependência externa.

* `requirement-sample.json` — requisito de exemplo
* `project-metadata-sample.json` — metadados do projeto alvo

---

## `docs/`

Documentação de referência.

* `comparison.md` — comparação entre ferramentas e justificativa da escolha
* `steps-and-resources.md` — passos do processo e recursos envolvidos

---

## `workspace/`

Área temporária de trabalho do fluxo. Descartável.

* `repos/` — repositórios clonados
* `artifacts/` — artefatos gerados (specs, backlogs)
* `temp/` — arquivos transitórios

---

## `logs/`

Logs de execução por run. Rastreabilidade e auditoria.

---

# Estrutura em `src/AutonomousSoftwareFactory/`

Projeto único tipo console. Sem camadas separadas.

## `Models/`

Classes que mapeiam os YAMLs:

* `WorkflowDefinition` — representa workflow.yaml
* `StepDefinition` — cada step do workflow
* `AgentDefinition` — cada agente do agents.yaml
* `SkillDefinition` — cada skill do skills_registry.yaml
* `ToolDefinition` — cada tool do tools.yaml
* `ExecutionContext` — estado compartilhado entre steps
* `ExecutionResult` — resultado de cada execução

## `Yaml/`

* `YamlConfigLoader` — lê e deserializa todos os YAMLs em Models

## `Workflow/`

* `IWorkflowEngine` — contrato do engine
* `WorkflowEngine` — executa steps sequencialmente, resolve agentes, persiste estado

## `Agents/`

* `IAgentExecutor` — contrato de execução de agente
* `AgentExecutor` — monta prompt (agente + skills + contexto), chama LLM, parseia output

## `Tools/`

* `IToolExecutor` — contrato de execução de tool
* `ToolExecutor` — executa comandos de terminal ou chamadas de API conforme tools.yaml

## `Llm/`

* `ILlmClient` — contrato do cliente LLM
* `LlmClient` — chamada ao modelo (OpenAI ou compatível)

## `State/`

* `IStateStore` — contrato de armazenamento de estado
* `InMemoryStateStore` — dicionário em memória com outputs de cada step

---

# Contratos do código

```csharp
public interface IWorkflowEngine
{
    Task<ExecutionResult> ExecuteAsync(ExecutionContext context, CancellationToken ct);
}

public interface IAgentExecutor
{
    Task<AgentResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct);
}

public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken ct);
}

public interface ILlmClient
{
    Task<string> CompleteAsync(string prompt, CancellationToken ct);
}

public interface IStateStore
{
    void Set(string key, object value);
    T Get<T>(string key);
    bool Has(string key);
}

public interface IYamlConfigLoader
{
    WorkflowDefinition LoadWorkflow(string path);
    List<AgentDefinition> LoadAgents(string path);
    List<SkillDefinition> LoadSkills(string path);
    List<ToolDefinition> LoadTools(string path);
}
```

---

# Fluxo de execução

1. `Program.cs` lê `appsettings.json`
2. `YamlConfigLoader` carrega workflow, agents, skills e tools
3. Lê o requisito de entrada (arquivo JSON ou argumento)
4. Cria `ExecutionContext` com inputs
5. `WorkflowEngine` percorre os steps sequencialmente
6. Para cada step:
   * resolve o agente pelo nome
   * monta prompt com skills + contexto + inputs do step
   * chama LLM via `ILlmClient`
   * se o agente tem tools, executa via `IToolExecutor`
   * salva output no `IStateStore`
7. Em caso de falha, registra log e para (ou retenta conforme policy)
8. Ao final, consolida resultado

---

# Configuração (`appsettings.json`)

```json
{
  "Workspace": {
    "BasePath": "./workspace"
  },
  "Configs": {
    "BasePath": "./configs"
  },
  "GitHub": {
    "Token": "",
    "ApiUrl": "https://api.github.com"
  },
  "Llm": {
    "Provider": "OpenAI",
    "Model": "gpt-4.1",
    "ApiKey": ""
  },
  "Execution": {
    "MaxRetries": 2,
    "LogPath": "./logs"
  }
}
```

---

# Detecção de stack do projeto-alvo

O `ToolExecutor` resolve qual comando usar baseado no `project_context`:

* `.csproj` → .NET (`dotnet build`, `dotnet test`)
* `pom.xml` → Java/Maven (`mvn clean install`, `mvn test`)
* `package.json` → Node (`npm install`, `npm run build`, `npm test`)

Não precisa de classe separada para isso. Uma condição simples no `ToolExecutor` resolve.

---

# Princípios

1. **YAML define, código executa** — configs YAML são contrato, não lógica
2. **Agente pensa, tool executa** — LLM decide; runtime executa ações reais
3. **Estado explícito** — cada step salva output no StateStore
4. **Logs por step** — tudo rastreável
5. **Projeto simples** — um projeto .NET, sem camadas desnecessárias

---

# Ordem de implementação

## Fase 1 — Fundação

* `Program.cs` + DI
* `YamlConfigLoader` (deserializar YAMLs)
* `Models/` (classes que mapeiam os YAMLs)
* `InMemoryStateStore`
* `WorkflowEngine` (loop de steps)

## Fase 2 — Execução

* `LlmClient` (chamada ao modelo)
* `AgentExecutor` (prompt + LLM + parse output)
* `ToolExecutor` (execução de comandos)

## Fase 3 — Integração

* Git operations (clone, branch, commit, push, PR)
* Build e testes por stack
* Logs de execução

## Fase 4 — Completo

* Pipeline end-to-end funcional
* Samples testáveis
* Scripts de automação

---