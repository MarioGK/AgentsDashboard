import fs from 'node:fs';
import path from 'node:path';

export interface WorkflowEnvironment
{
    baseUrl: string;
    zaiApiKey: string;
    repoRemotePath: string;
    repoCloneRoot: string;
}

function readRequiredEnvironmentVariable(name: string): string
{
    const value = process.env[name]?.trim() ?? '';
    if (value.length === 0)
    {
        throw new Error(`Missing required environment variable: ${name}`);
    }

    return value;
}

function assertAbsolutePath(name: string, value: string): string
{
    if (!path.isAbsolute(value))
    {
        throw new Error(`Environment variable ${name} must be an absolute path. Received: ${value}`);
    }

    return path.normalize(value);
}

export function resolveWorkflowEnvironment(): WorkflowEnvironment
{
    const baseUrl = process.env.BASE_URL?.trim() || 'http://127.0.0.1:5266';
    const zaiApiKey = readRequiredEnvironmentVariable('PLAYWRIGHT_E2E_ZAI_API_KEY');
    const repoRemotePath = assertAbsolutePath(
        'PLAYWRIGHT_E2E_REPO_REMOTE_PATH',
        readRequiredEnvironmentVariable('PLAYWRIGHT_E2E_REPO_REMOTE_PATH'));
    const repoCloneRoot = assertAbsolutePath(
        'PLAYWRIGHT_E2E_REPO_CLONE_ROOT',
        readRequiredEnvironmentVariable('PLAYWRIGHT_E2E_REPO_CLONE_ROOT'));

    if (!fs.existsSync(repoRemotePath))
    {
        throw new Error(`PLAYWRIGHT_E2E_REPO_REMOTE_PATH does not exist: ${repoRemotePath}`);
    }

    fs.mkdirSync(repoCloneRoot, { recursive: true });

    return {
        baseUrl,
        zaiApiKey,
        repoRemotePath,
        repoCloneRoot,
    };
}
