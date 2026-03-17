# Tarefas de Implementação — v1

Referência: [project-structure.md](project-structure.md)

---

## Fase 1 — Fundação

### 1.1 Criar estrutura do projeto .NET

- [x] Criar solution `AutonomousSoftwareFactory.sln`
- [x] Criar projeto console `src/AutonomousSoftwareFactory/AutonomousSoftwareFactory.csproj` (.NET 8)
- [x] Adicionar pacotes NuGet: `YamlDotNet`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.DependencyInjection`
- [x] Criar projeto de testes `tests/AutonomousSoftwareFactory.Tests/AutonomousSoftwareFactory.Tests.csproj` (xUnit)
- [x] Validar que `dotnet build` compila sem erros

### 1.2 Models — classes que mapeiam os YAMLs

Cada classe mapeia a estrutura dos YAMLs em `configs/`.

- [x] `Models/WorkflowDefinition.cs` — mapeia `workflow.yaml` (name, description, execution, context, policies, steps)
- [x] `Models/StepDefinition.cs` — cada step do workflow (id, name, type, agent, input, output, next, retry, on_failure, validations)
- [x] `Models/AgentDefinition.cs` — cada agente do `agents.yaml` (name, description, status, responsibilities, input, output, skills, tools, prompt)
- [x] `Models/SkillDefinition.cs` — cada skill do `skills_registry.yaml` (name, type, description, instructions, expected_input, expected_output, constraints, tools)
- [x] `Models/ToolDefinition.cs` — cada tool do `tools.yaml` (name, category, description, execution_type, input, output, command, api, constraints)
- [x] `Models/PromptDefinition.cs` — cada prompt do `prompts.yaml` (chave, description, template)
- [x] `Models/ExecutionContext.cs` — estado compartilhado entre steps (inputs, shared_state, current_step)
- [x] `Models/ExecutionResult.cs` — resultado final da execução (status, outputs, errors, duration)

### 1.3 Models — classes de execução

- [x] `Models/AgentExecutionRequest.cs` — dados de entrada para execução de um agente (agent, inputs, skills, tools, prompt, context)
- [x] `Models/AgentResult.cs` — resultado retornado pelo agente (status, data, message)
- [x] `Models/ToolExecutionRequest.cs` — dados de entrada para execução de uma tool (tool, inputs, working_directory)
- [x] `Models/ToolResult.cs` — resultado retornado pela tool (success, output, errors)

### 1.4 YamlConfigLoader

- [x] `Yaml/YamlConfigLoader.cs` — implementar `IYamlConfigLoader`
  - [x] `LoadWorkflow(string path)` → `WorkflowDefinition`
  - [x] `LoadAgents(string path)` → `List<AgentDefinition>`
  - [x] `LoadSkills(string path)` → `List<SkillDefinition>`
  - [x] `LoadTools(string path)` → `List<ToolDefinition>`
  - [x] `LoadPrompts(string path)` → `List<PromptDefinition>`
- [x] Testar deserialização com os YAMLs reais em `configs/`

### 1.5 State Store

- [x] `State/IStateStore.cs` — interface com `Set(key, value)`, `Get<T>(key)`, `Has(key)`
- [x] `State/InMemoryStateStore.cs` — implementação com `Dictionary<string, object>`
- [x] Testes unitários do InMemoryStateStore

### 1.6 Workflow Engine

- [x] `Workflow/IWorkflowEngine.cs` — interface com `ExecuteAsync(ExecutionContext, CancellationToken)`
- [x] `Workflow/WorkflowEngine.cs` — implementação:
  - [x] Receber workflow, agents, skills, tools e prompts carregados
  - [x] Percorrer steps sequencialmente seguindo `next`
  - [x] Resolver step type `input` — validar campos obrigatórios e salvar no state
  - [x] Resolver step type `agent` — localizar agente pelo nome, montar request, chamar AgentExecutor
  - [x] Resolver step type `output` — consolidar resultado final
  - [x] Resolver inputs com template `{{steps.X.output.Y}}` e `{{context.inputs.Z}}`
  - [x] Salvar output de cada step no StateStore
  - [x] Aplicar política de retry por step (max_attempts, strategy)
  - [x] Aplicar on_failure (stop ou continue)
  - [x] Log por step (início, fim, status, erros)

### 1.7 Program.cs + DI

- [x] `Program.cs` — entry point:
  - [x] Ler `appsettings.json` com `IConfiguration`
  - [x] Registrar serviços via DI (`IYamlConfigLoader`, `IStateStore`, `IWorkflowEngine`, `IAgentExecutor`, `IToolExecutor`, `ILlmClient`)
  - [x] Carregar YAMLs de `configs/`
  - [x] Ler argumento `--requirement` (caminho do JSON de requisito)
  - [x] Criar `ExecutionContext` com inputs
  - [x] Chamar `WorkflowEngine.ExecuteAsync`
  - [x] Exibir resultado final

### 1.8 Testes da Fase 1

- [x] Teste: deserializar `configs/workflow.yaml` e validar steps carregados
- [x] Teste: deserializar `configs/agents.yaml` e validar agentes carregados
- [x] Teste: deserializar `configs/skills_registry.yaml` e validar skills
- [x] Teste: deserializar `configs/tools.yaml` e validar tools
- [x] Teste: deserializar `configs/prompts.yaml` e validar prompts
- [x] Teste: InMemoryStateStore — Set, Get, Has
- [x] Teste: WorkflowEngine — executar workflow mínimo com steps de input e output
- [x] Validar que `dotnet test` passa sem erros

---

## Fase 2 — Execução

### 2.1 LLM Client

- [x] `Llm/ILlmClient.cs` — interface com `CompleteAsync(string prompt, CancellationToken)`
- [x] `Llm/LlmClient.cs` — implementação:
  - [x] Ler configuração de `appsettings.json` (Provider, Model, ApiKey)
  - [x] Chamar API OpenAI (ou compatível) via `HttpClient`
  - [x] Enviar prompt como mensagem e retornar resposta como string
  - [x] Tratar erros HTTP e timeouts
  - [x] Log da chamada (prompt resumido, tokens, duração)

### 2.2 Agent Executor

- [x] `Agents/IAgentExecutor.cs` — interface com `ExecuteAsync(AgentExecutionRequest, CancellationToken)`
- [x] `Agents/AgentExecutor.cs` — implementação:
  - [x] Receber `AgentExecutionRequest` com agente, inputs e contexto
  - [x] Montar prompt final: system prompt + context injection + prompt do agente + inputs do step
  - [x] Injetar skills do agente como instruções no prompt
  - [x] Chamar `ILlmClient.CompleteAsync`
  - [x] Parsear resposta JSON do LLM
  - [x] Se o agente tem tools, identificar tools a executar na resposta
  - [x] Chamar `IToolExecutor` para cada tool necessária
  - [x] Retornar `AgentResult` com status, data e message

### 2.3 Tool Executor

- [x] `Tools/IToolExecutor.cs` — interface com `ExecuteAsync(ToolExecutionRequest, CancellationToken)`
- [x] `Tools/ToolExecutor.cs` — implementação:
  - [x] Resolver tool pelo nome no catálogo carregado
  - [x] Para `execution_type: command`:
    - [x] Substituir placeholders `{{input}}` no template do comando
    - [x] Executar processo externo (`Process.Start`)
    - [x] Capturar stdout e stderr
    - [x] Retornar `ToolResult` com success, output e errors
  - [x] Para `execution_type: api`:
    - [x] Montar request HTTP com endpoint, method, headers e body
    - [x] Substituir placeholders nos templates
    - [x] Executar chamada via `HttpClient`
    - [x] Retornar `ToolResult` com resposta da API
  - [x] Para `execution_type: internal`:
    - [x] Implementar `read_files` — ler arquivo do workspace
    - [x] Implementar `list_directory` — listar diretório
    - [x] Implementar `search_files` — buscar padrão em arquivos
    - [x] Implementar `write_file` — escrever em arquivo existente
    - [x] Implementar `create_file` — criar novo arquivo
    - [x] Implementar `delete_file` — remover arquivo
    - [x] Validar que caminhos estão dentro do workspace
  - [x] Detecção de stack do projeto-alvo:
    - [x] `.csproj` → usar comandos `dotnet`
    - [x] `pom.xml` → usar comandos `mvn`
    - [x] `package.json` → usar comandos `npm`

### 2.4 Testes da Fase 2

- [x] Teste: LlmClient — mock de HttpClient, validar montagem do request
- [x] Teste: AgentExecutor — mock de ILlmClient, validar montagem de prompt e parse de output
- [x] Teste: ToolExecutor — execution_type `internal` (read_files, list_directory, write_file)
- [x] Teste: ToolExecutor — execution_type `command` com comando simples
- [x] Teste: integração AgentExecutor + ToolExecutor com mock de LLM

---

## Fase 3 — Integração

### 3.1 Git Operations

- [x] ToolExecutor: `git_clone` — clonar repositório para `workspace/repos/`
- [x] ToolExecutor: `git_branch` — criar branch no repositório clonado
- [x] ToolExecutor: `git_commit` — add + commit com mensagem
- [x] ToolExecutor: `git_push` — push para remote
- [x] ToolExecutor: `create_pull_request` — chamada à GitHub API para criar PR
- [x] Validar que token GitHub é lido de `appsettings.json` e injetado nos headers

### 3.2 Build e Testes por stack

- [x] ToolExecutor: `dotnet_build` / `dotnet_test` / `dotnet_restore`
- [x] ToolExecutor: `maven_build` / `junit_test` / `maven_install`
- [x] ToolExecutor: `npm_build` / `jest_test` / `npm_install`
- [x] ToolExecutor: `eslint` / `dotnet_format` / `checkstyle`

### 3.3 Logs de execução

- [x] Criar sistema de log por run em `logs/`
  - [x] Nome do arquivo: `{timestamp}-{workflow-name}.log`
  - [x] Log estruturado: step, status, duração, erros
  - [x] Log de cada chamada LLM (prompt resumido, resposta resumida)
  - [x] Log de cada tool executada (comando, output, erros)

### 3.4 Testes da Fase 3

- [x] Teste: git_clone em repositório local de teste
- [x] Teste: create_pull_request com mock da GitHub API
- [x] Teste: build commands com mock de Process
- [x] Teste: geração de log file com conteúdo esperado

---

## Fase 4 — Pipeline Completo

### 4.1 Execução end-to-end

- [x] Executar pipeline completo com `samples/requirement-sample.json` + `samples/project-metadata-sample.json`
- [x] Validar que todos os 16 steps executam na sequência correta
- [x] Validar que outputs de cada step ficam disponíveis para o próximo
- [x] Validar que retry funciona em steps com on_failure
- [x] Validar que o PR é criado no GitHub (ou simular com mock)

### 4.2 Checkpoints e recuperação

- [ ] Implementar persistência de checkpoints (salvar state em arquivo após cada step)
- [ ] Implementar retomada de execução a partir de checkpoint
- [ ] Teste: interromper execução no step 5 e retomar do checkpoint

### 4.3 Scripts de automação

- [ ] Atualizar `scripts/setup-env.ps1` — incluir `dotnet restore` do projeto
- [ ] Atualizar `scripts/run-local.ps1` — validar que projeto compila antes de executar
- [ ] Testar execução via `scripts/run-local.ps1 -Requirement ./samples/requirement-sample.json`

### 4.4 Validação final

- [ ] `dotnet build` sem warnings
- [ ] `dotnet test` com todos os testes passando
- [ ] Pipeline executa do início ao fim com saída estruturada
- [ ] README.md atualizado com instruções de execução reais
- [ ] Logs gerados em `logs/` com rastreabilidade completa

---

## Resumo por fase

| Fase | Foco | Entregas |
|------|------|----------|
| 1 | Fundação | Solution, Models, YamlConfigLoader, StateStore, WorkflowEngine, Program.cs |
| 2 | Execução | LlmClient, AgentExecutor, ToolExecutor |
| 3 | Integração | Git operations, build/test por stack, logs |
| 4 | Completo | Pipeline end-to-end, checkpoints, scripts, validação final |
