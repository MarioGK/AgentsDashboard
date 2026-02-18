import path from 'node:path';

export interface WorkflowTestData
{
    repositoryName: string;
    taskName: string;
    localClonePath: string;
    taskPrompt: string;
    runGuidance: string;
}

export function createWorkflowTestData(repoCloneRoot: string): WorkflowTestData
{
    const id = `${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`;
    const repositoryName = `pw-zai-repo-${id}`;
    const taskName = `pw-zai-task-${id}`;
    const localClonePath = path.join(repoCloneRoot, repositoryName);

    return {
        repositoryName,
        taskName,
        localClonePath,
        taskPrompt: [
            'You are running inside the repository workspace.',
            'Make a small deterministic edit to README.md and keep output concise.',
            'Finish with a successful completion envelope.',
        ].join('\n'),
        runGuidance: [
            'Create or update README.md with a line containing:',
            `"playwright-zai-e2e-${id}"`,
            'Then complete successfully.',
        ].join('\n'),
    };
}
