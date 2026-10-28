"""Run the real bootstrap/configuration scripts with recorded fake CLIs."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SUB = '44444444-4444-4444-8444-444444444444'
CLIENT = '11111111-1111-4111-8111-111111111111'
TENANT = '22222222-2222-4222-8222-222222222222'
PRINCIPAL = '33333333-3333-4333-8333-333333333333'
FAKE_CLI = r'''#!/usr/bin/env python3
import json, os, pathlib, sys
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
body = sys.stdin.read() if '--input' in args else ''
with open(os.environ['CLI_LOG'], 'a') as out:
    out.write(json.dumps(dict(command=name, args=args, body=body)) + '\n')
if name == 'az':
    if args[:2] == ['account', 'show'] and '--query' in args:
        print('22222222-2222-4222-8222-222222222222')
    elif args[:2] == ['provider', 'show']:
        print(os.environ.get('PROVIDER_STATE', 'Registered'))
    elif args[:2] == ['identity', 'create']:
        print(json.dumps(dict(clientId='11111111-1111-4111-8111-111111111111', principalId='33333333-3333-4333-8333-333333333333')))
    elif args[:3] == ['role', 'assignment', 'list']:
        print('existing-role' if os.environ.get('EXISTING_ROLES') == '1' else '')
elif name == 'gh':
    if args and args[0] == 'api' and os.environ.get('GITHUB_DENIED') == '1':
        print('gh: Resource not accessible by integration (HTTP 403)', file=sys.stderr)
        sys.exit(1)
    if args and args[0] == 'api' and any(arg.endswith('/environments/production') for arg in args) and '--method' not in args:
        if os.environ.get('ENVIRONMENT_MISSING') == '1':
            print('gh: Not Found (HTTP 404)', file=sys.stderr)
            sys.exit(1)
        print('custom')
    if '--jq' in args and 'select(.name !='  in args[-1] and os.environ.get('EXTRA_BRANCH'):
        print('another-branch')
elif name == 'curl':
    counter = pathlib.Path(os.environ['CLI_LOG'] + '.curl-count')
    count = int(counter.read_text()) if counter.exists() else 0
    counter.write_text(str(count + 1))
    if count == 0:
        print('Service warming up', file=sys.stderr)
        sys.exit(22)
    print('{"status":"not-ready"}' if os.environ.get('READINESS_NEVER_READY') else '{"status":"ready"}')
'''


class DeploymentScripts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.log = self.directory / 'commands.jsonl'
        self.config = self.directory / 'bootstrap.json'
        self.bin = self.directory / 'bin'
        self.bin.mkdir()
        for command in ('az', 'gh', 'curl', 'sleep'):
            path = self.bin / command
            path.write_text(FAKE_CLI)
            path.chmod(0o755)
        self.env = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ['PATH'], CLI_LOG=str(self.log))

    def run_script(self, script, *args, **env):
        return subprocess.run(['bash', 'scripts/' + script, *args], cwd=ROOT,
                              env=dict(self.env, **env), capture_output=True, text=True)

    def commands(self, command=None):
        rows = [json.loads(line) for line in self.log.read_text().splitlines()] if self.log.exists() else []
        return [row for row in rows if command is None or row['command'] == command]

    def bootstrap(self, **env):
        return self.run_script('bootstrap-azure.sh', '--subscription', SUB,
                               '--location', 'australiaeast', '--output', str(self.config), **env)

    def test_bootstrap_creates_exact_oidc_trust_and_group_scoped_roles(self):
        result = self.bootstrap()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(self.config.read_text()), dict(
            AZURE_CLIENT_ID=CLIENT, AZURE_TENANT_ID=TENANT,
            AZURE_SUBSCRIPTION_ID=SUB, AZURE_RESOURCE_GROUP='badgecluster'))
        az = self.commands('az')
        federation = next(row['args'] for row in az if row['args'][:3] == ['identity', 'federated-credential', 'create'])
        self.assertEqual(federation[federation.index('--subject')+1], 'repo:dylan-mccarthy/BadgeCluster:environment:production')
        self.assertIn('api://AzureADTokenExchange', federation)
        roles = [row['args'] for row in az if row['args'][:3] == ['role', 'assignment', 'create']]
        self.assertEqual(len(roles), 2)
        for role in roles:
            self.assertEqual(role[role.index('--scope')+1], f'/subscriptions/{SUB}/resourceGroups/badgecluster')
            self.assertEqual(role[role.index('--assignee-object-id')+1], PRINCIPAL)
        self.assertTrue(all('--subscription' in row['args'] for row in az))
        self.assertFalse(any('secret' in row['args'] for row in az))

    def test_rerun_reuses_existing_role_assignments(self):
        result = self.bootstrap(EXISTING_ROLES='1')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(any(row['args'][:3] == ['role', 'assignment', 'create'] for row in self.commands('az')))

    def test_unregistered_providers_are_registered(self):
        result = self.bootstrap(PROVIDER_STATE='NotRegistered')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(sum(row['args'][:2] == ['provider', 'register'] for row in self.commands('az')), 3)

    def test_invalid_account_input_fails_before_azure_calls(self):
        for args in (('--subscription', 'not-a-uuid', '--location', 'australiaeast'),
                     ('--subscription', SUB, '--location', 'Australia East'),
                     ('--subscription', SUB, '--location', 'australiaeast', '--resource-group', '../group')):
            result = self.run_script('bootstrap-azure.sh', *args)
            self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.commands(), [])

    def test_configuration_requires_main_and_valid_ids(self):
        settings = dict(GITHUB_REF='refs/heads/main', AZURE_CLIENT_ID=CLIENT,
                        AZURE_TENANT_ID=TENANT, AZURE_SUBSCRIPTION_ID=SUB, AZURE_RESOURCE_GROUP='badgecluster')
        self.assertEqual(self.run_script('check-deployment-config.sh', **settings).returncode, 0)
        for change in (dict(GITHUB_REF='refs/heads/other'), dict(AZURE_CLIENT_ID=''),
                       dict(AZURE_SUBSCRIPTION_ID='invalid'), dict(APP_SERVICE_SKU='F1')):
            self.assertNotEqual(self.run_script('check-deployment-config.sh', **dict(settings, **change)).returncode, 0)

    def test_github_setup_writes_only_four_nonsecret_environment_variables(self):
        self.assertEqual(self.bootstrap().returncode, 0)
        result = self.run_script('configure-github.sh', str(self.config))
        self.assertEqual(result.returncode, 0, result.stderr)
        commands = self.commands('gh')
        variables = [row['args'] for row in commands if row['args'][:2] == ['variable', 'set']]
        self.assertEqual({args[2] for args in variables}, set(json.loads(self.config.read_text())))
        self.assertTrue(all(args[args.index('--env')+1] == 'production' for args in variables))
        branch = next(row for row in commands if '--method' in row['args'] and 'POST' in row['args'])
        self.assertEqual(json.loads(branch['body']), dict(name='main', type='branch'))
        self.assertFalse(any(row['args'][:2] == ['secret', 'set'] for row in commands))

    def test_github_permission_failure_does_not_write_variables(self):
        self.assertEqual(self.bootstrap().returncode, 0)
        result = self.run_script('configure-github.sh', str(self.config), GITHUB_DENIED='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('403', result.stderr)
        self.assertFalse(any(row['args'][:2] == ['variable', 'set'] for row in self.commands('gh')))

    def test_existing_extra_branch_policy_blocks_setup(self):
        result = self.run_script('configure-github-environment.sh', EXTRA_BRANCH='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('additional branch/tag policies', result.stderr)

    def test_readiness_retries_transient_failure_and_requires_ready_json(self):
        result = self.run_script('check-readiness.sh', 'https://badgecluster-example.azurewebsites.net')
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(self.commands('curl')), 2)
        self.assertEqual(len(self.commands('sleep')), 1)

    def test_readiness_fails_after_bounded_retries_on_nonready_json(self):
        result = self.run_script('check-readiness.sh', 'https://badgecluster-example.azurewebsites.net', READINESS_NEVER_READY='1')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(len(self.commands('curl')), 24)

    def test_new_github_environment_gets_explicit_branch_policy(self):
        result = self.run_script('configure-github-environment.sh', ENVIRONMENT_MISSING='1')
        self.assertEqual(result.returncode, 0, result.stderr)
        creation = next(row for row in self.commands('gh') if 'PUT' in row['args'])
        self.assertEqual(json.loads(creation['body']), dict(deployment_branch_policy=dict(
            protected_branches=False, custom_branch_policies=True)))

    def test_readiness_rejects_other_origins(self):
        result = self.run_script('check-readiness.sh', 'http://example.com')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.commands('curl'), [])


if __name__ == '__main__':
    unittest.main()
