# 1. Passos do processo

1. Receber o requisito de entrada.
2. Identificar o projeto, repositório e stack tecnológica.
3. Baixar o código-fonte do repositório.
4. Ler e processar o codebase para entendimento do sistema.
5. Gerar backlog inicial de features.
6. Quebrar cada feature em microtarefas.
7. Enriquecer as microtarefas com contexto técnico e de negócio.
8. Gerar specs, plan e tasks com base no requisito e no codebase.
9. Validar se o plano está coerente com a arquitetura existente.
10. Preparar o ambiente de execução da stack do projeto.
11. Implementar o código.
12. Baixar dependências necessárias.
13. Compilar ou buildar o projeto.
14. Executar testes automatizados.
15. Rodar validações de qualidade, lint e análise estática.
16. Revisar o resultado final antes da entrega.
17. Gerar branch, commit e pull request.
18. Encerrar o fluxo com logs, rastreabilidade e status final.

---

# 2. Recursos, ferramentas e serviços envolvidos

## Orquestração

1. Agent Framework para definir e executar o workflow.
2. YAML para descrever o fluxo declarativo.
3. .NET/C# para customizações e extensões do fluxo.

## Execução local

4. Máquina local com permissões de leitura, escrita e execução.
5. Diretório de trabalho temporário para clonar repositórios e gerar artefatos.
6. Runtime do agente e variáveis de ambiente configuradas.

## Repositório e versionamento

7. Git para clone, branch, commit e push.
8. GitHub como repositório inicial.
9. Token/PAT ou credencial com permissão para leitura e criação de PR.

## Leitura e entendimento do codebase

10. Parser/Indexer do codebase.
11. Serviço de embeddings ou indexação semântica, se desejar consulta inteligente.
12. Armazenamento local ou vetorial para índice do projeto.
13. Skills do projeto definindo linguagem, arquitetura e padrões esperados.

## Geração de artefatos de engenharia

14. Agente de backlog/features.
15. Agente de microtarefas.
16. Agente de specs/plan/tasks.
17. Templates de documentação para padronizar saída.

## Implementação

18. Agente codificador.
19. Gerenciador de dependências da stack.
20. Acesso à internet ou repositório interno para baixar pacotes.

## Build e execução por stack

21. .NET SDK para projetos C#.
22. Java JDK + Maven/Gradle para Java/Quarkus.
23. Node.js + npm/pnpm/yarn para frontend Angular/React/Vue.
24. Ferramentas específicas da stack conforme o projeto exigir.

## Testes e qualidade

25. Framework de testes da stack.
26. Ferramenta de lint.
27. Ferramenta de análise estática de código.
28. Validador final antes do PR.

## Entrega

29. Integração com GitHub Pull Request API ou automação Git.
30. Geração automática de branch, commit message e descrição do PR.

## Observabilidade e controle

31. Logs de execução do workflow.
32. Rastreamento por etapa, status e erros.
33. Armazenamento de artefatos gerados.
34. Checkpoints para retomada em caso de falha.

## Ambiente produtivo

35. Azure AI Foundry para hospedar e operar a solução em produção.
36. Credenciais seguras para acesso a Git, pacotes e serviços externos.
37. Mecanismo de acionamento do fluxo em produção.
38. Monitoramento da execução, falhas e auditoria.

## Segurança e permissões

39. Permissão para clonar repositório.
40. Permissão para instalar dependências.
41. Permissão para executar build e testes.
42. Permissão para criar branch, push e pull request.
43. Gestão segura de secrets, tokens e chaves.