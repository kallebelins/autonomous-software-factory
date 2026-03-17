# Tarefas de Implementação — v1

Referência: [project-structure.md](project-structure.md)

---

## Fase 1 — Fundação

### 1.1 Criar estrutura do projeto .NET

- [ ] Criar solution `AutonomousSoftwareFactory.sln`
- [ ] Criar projeto console `src/AutonomousSoftwareFactory/AutonomousSoftwareFactory.csproj` (.NET 8)
- [ ] Adicionar pacotes NuGet: `YamlDotNet`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.DependencyInjection`
- [ ] Criar projeto de testes `tests/AutonomousSoftwareFactory.Tests/AutonomousSoftwareFactory.Tests.csproj` (xUnit)
- [ ] Validar que `dotnet build` compila sem erros

### 1.2 Models — classes que mapeiam os YAMLs

Cada classe mapeia a estrutura dos YAMLs em `configs/`.

- [ ] `Models/WorkflowDefinition.cs` — mapeia `workflow.yaml` (name, description, execution, context, policies, steps)
- [ ] `Models/StepDefinition.cs` — cada step do workflow (id, name, type, agent, input, output, next, retry, on_failure, validations)
- [ ] `Models/AgentDefinition.cs` — cada agente do `agents.yaml` (name, description, status, responsibilities, input, output, skills, tools, prompt)
- [ ] `Models/SkillDefinition.cs` — cada skill do `skills_registry.yaml` (name, type, description, instructions, expected_input, expected_output, constraints, tools)
- [ ] `Models/ToolDefinition.cs` — cada tool do `tools.yaml` (name, category, description, execution_type, input, output, command, api, constraints)
- [ ] `Models/PromptDefinition.cs` — cada prompt do `prompts.yaml` (chave, description, template)
- [ ] `Models/ExecutionContext.cs` — estado compartilhado entre steps (inputs, shared_state, current_step)
- [ ] `Models/ExecutionResult.cs` — resultado final da execução (status, outputs, errors, duration)

### 1.3 Models — classes de execução

- [ ] `Models/AgentExecutionRequest.cs` — dados de entrada para execução de um agente (agent, inputs, skills, tools, prompt, context)
- [ ] `Models/AgentResult.cs` — resultado retornado pelo agente (status, data, message)
- [ ] `Models/ToolExecutionRequest.cs` — dados de entrada para execução de uma tool (tool, inputs, working_directory)
- [ ] `Models/ToolResult.cs` — resultado retornado pela tool (success, output, errors)

### 1.4 YamlConfigLoader

- [ ] `Yaml/YamlConfigLoader.cs` — implementar `IYamlConfigLoader`
  - [ ] `LoadWorkflow(string path)` → `WorkflowDefinition`
  - [ ] `LoadAgents(string path)` → `List<AgentDefinition>`
  - [ ] `LoadSkills(string path)` → `List<SkillDefinition>`
  - [ ] `LoadTools(string path)` → `List<ToolDefinition>`
  - [ ] `LoadPrompts(string path)` → `List<PromptDefinition>`
- [ ] Testar deserialização com os YAMLs reais em `configs/`

### 1.5 State Store

- [ ] `State/IStateStore.cs` — interface com `Set(key, value)`, `Get<T>(key)`, `Has(key)`
- [ ] `State/InMemoryStateStore.cs` — implementação com `Dictionary<string, object>`
- [ ] Testes unitários do InMemoryStateStore

### 1.6 Workflow Engine

- [ ] `Workflow/IWorkflowEngine.cs` — interface com `ExecuteAsync(ExecutionContext, CancellationToken)`
- [ ] `Workflow/WorkflowEngine.cs` — implementação:
  - [ ] Receber workflow, agents, skills, tools e prompts carregados
  - [ ] Percorrer steps sequencialmente seguindo `next`
  - [ ] Resolver step type `input` — validar campos obrigatórios e salvar no state
  - [ ] Resolver step type `agent` — localizar agente pelo nome, montar request, chamar AgentExecutor
  - [ ] Resolver step type `output` — consolidar resultado final
  - [ ] Resolver inputs com template `{{steps.X.output.Y}}` e `{{context.inputs.Z}}`
  - [ ] Salvar output de cada step no StateStore
  - [ ] Aplicar política de retry por step (max_attempts, strategy)
  - [ ] Aplicar on_failure (stop ou continue)
  - [ ] Log por step (início, fim, status, erros)

### 1.7 Program.cs + DI

- [ ] `Program.cs` — entry point:
  - [ ] Ler `appsettings.json` com `IConfiguration`
  - [ ] Registrar serviços via DI (`IYamlConfigLoader`, `IStateStore`, `IWorkflowEngine`, `IAgentExecutor`, `IToolExecutor`, `ILlmClient`)
  - [ ] Carregar YAMLs de `configs/`
  - [ ] Ler argumento `--requirement` (caminho do JSON de requisito)
  - [ ] Criar `ExecutionContext` com inputs
  - [ ] Chamar `WorkflowEngine.ExecuteAsync`
  - [ ] Exibir resultado final

### 1.8 Testes da Fase 1

- [ ] Teste: deserializar `configs/workflow.yaml` e validar steps carregados
- [ ] Teste: deserializar `configs/agents.yaml` e validar agentes carregados
- [ ] Teste: deserializar `configs/skills_registry.yaml` e validar skills
- [ ] Teste: deserializar `configs/tools.yaml` e validar tools
- [ ] Teste: deserializar `configs/prompts.yaml` e validar prompts
- [ ] Teste: InMemoryStateStore — Set, Get, Has
- [ ] Teste: WorkflowEngine — executar workflow mínimo com steps de input e output
- [ ] Validar que `dotnet test` passa sem erros

---

## Fase 2 — Execução

### 2.1 LLM Client

- [ ] `Llm/ILlmClient.cs` — interface com `CompleteAsync(string prompt, CancellationToken)`
- [ ] `Llm/LlmClient.cs` — implementação:
  - [ ] Ler configuração de `appsettings.json` (Provider, Model, ApiKey)
  - [ ] Chamar API OpenAI (ou compatível) via `HttpClient`
  - [ ] Enviar prompt como mensagem e retornar resposta como string
  - [ ] Tratar erros HTTP e timeouts
  - [ ] Log da chamada (prompt resumido, tokens, duração)

### 2.2 Agent Executor

- [ ] `Agents/IAgentExecutor.cs` — interface com `ExecuteAsync(AgentExecutionRequest, CancellationToken)`
- [ ] `Agents/AgentExecutor.cs` — implementação:
  - [ ] Receber `AgentExecutionRequest` com agente, inputs e contexto
  - [ ] Montar prompt final: system prompt + context injection + prompt do agente + inputs do step
  - [ ] Injetar skills do agente como instruções no prompt
  - [ ] Chamar `ILlmClient.CompleteAsync`
  - [ ] Parsear resposta JSON do LLM
  - [ ] Se o agente tem tools, identificar tools a executar na resposta
  - [ ] Chamar `IToolExecutor` para cada tool necessária
  - [ ] Retornar `AgentResult` com status, data e message

### 2.3 Tool Executor

- [ ] `Tools/IToolExecutor.cs` — interface com `ExecuteAsync(ToolExecutionRequest, CancellationToken)`
- [ ] `Tools/ToolExecutor.cs` — implementação:
  - [ ] Resolver tool pelo nome no catálogo carregado
  - [ ] Para `execution_type: command`:
    - [ ] Substituir placeholders `{{input}}` no template do comando
    - [ ] Executar processo externo (`Process.Start`)
    - [ ] Capturar stdout e stderr
    - [ ] Retornar `ToolResult` com success, output e errors
  - [ ] Para `execution_type: api`:
    - [ ] Montar request HTTP com endpoint, method, headers e body
    - [ ] Substituir placeholders nos templates
    - [ ] Executar chamada via `HttpClient`
    - [ ] Retornar `ToolResult` com resposta da API
  - [ ] Para `execution_type: internal`:
    - [ ] Implementar `read_files` — ler arquivo do workspace
    - [ ] Implementar `list_directory` — listar diretório
    - [ ] Implementar `search_files` — buscar padrão em arquivos
    - [ ] Implementar `write_file` — escrever em arquivo existente
    - [ ] Implementar `create_file` — criar novo arquivo
    - [ ] Implementar `delete_file` — remover arquivo
    - [ ] Validar que caminhos estão dentro do workspace
  - [ ] Detecção de stack do projeto-alvo:
    - [ ] `.csproj` → usar comandos `dotnet`
    - [ ] `pom.xml` → usar comandos `mvn`
    - [ ] `package.json` → usar comandos `npm`

### 2.4 Testes da Fase 2

- [ ] Teste: LlmClient — mock de HttpClient, validar montagem do request
- [ ] Teste: AgentExecutor — mock de ILlmClient, validar montagem de prompt e parse de output
- [ ] Teste: ToolExecutor — execution_type `internal` (read_files, list_directory, write_file)
- [ ] Teste: ToolExecutor — execution_type `command` com comando simples
- [ ] Teste: integração AgentExecutor + ToolExecutor com mock de LLM

---

## Fase 3 — Integração

### 3.1 Git Operations

- [ ] ToolExecutor: `git_clone` — clonar repositório para `workspace/repos/`
- [ ] ToolExecutor: `git_branch` — criar branch no repositório clonado
- [ ] ToolExecutor: `git_commit` — add + commit com mensagem
- [ ] ToolExecutor: `git_push` — push para remote
- [ ] ToolExecutor: `create_pull_request` — chamada à GitHub API para criar PR
- [ ] Validar que token GitHub é lido de `appsettings.json` e injetado nos headers

### 3.2 Build e Testes por stack

- [ ] ToolExecutor: `dotnet_build` / `dotnet_test` / `dotnet_restore`
- [ ] ToolExecutor: `maven_build` / `junit_test` / `maven_install`
- [ ] ToolExecutor: `npm_build` / `jest_test` / `npm_install`
- [ ] ToolExecutor: `eslint` / `dotnet_format` / `checkstyle`

### 3.3 Logs de execução

- [ ] Criar sistema de log por run em `logs/`
  - [ ] Nome do arquivo: `{timestamp}-{workflow-name}.log`
  - [ ] Log estruturado: step, status, duração, erros
  - [ ] Log de cada chamada LLM (prompt resumido, resposta resumida)
  - [ ] Log de cada tool executada (comando, output, erros)

### 3.4 Testes da Fase 3

- [ ] Teste: git_clone em repositório local de teste
- [ ] Teste: create_pull_request com mock da GitHub API
- [ ] Teste: build commands com mock de Process
- [ ] Teste: geração de log file com conteúdo esperado

---

## Fase 4 — Pipeline Completo

### 4.1 Execução end-to-end

- [ ] Executar pipeline completo com `samples/requirement-sample.json` + `samples/project-metadata-sample.json`
- [ ] Validar que todos os 16 steps executam na sequência correta
- [ ] Validar que outputs de cada step ficam disponíveis para o próximo
- [ ] Validar que retry funciona em steps com on_failure
- [ ] Validar que o PR é criado no GitHub (ou simular com mock)

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
