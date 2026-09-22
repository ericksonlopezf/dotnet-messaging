// Copyright © Erickson Lopez. MIT License.
const assert = require('assert');
const {
  loadThresholds,
  parseScoreFromDescription,
  evaluateScore,
  verifyMutationGate,
  MAX_REPORT_AGE_DAYS
} = require('./verify-mutation-gate');

console.log('Running tests for verify-mutation-gate.js (4 Invariant Conditions)...\n');

const describe = (name, fn) => fn();
const it = (name, fn) => fn();

describe('verify-mutation-gate unit tests', () => {
  // Test 1: loadThresholds from stryker-config.json
  it('loadThresholds loads correct values from stryker-config.json', () => {
    const thresholds = loadThresholds();
    assert.strictEqual(thresholds.high, 100, 'Threshold high should be 100');
    assert.strictEqual(thresholds.low, 98, 'Threshold low should be 98');
    assert.strictEqual(thresholds.break, 95, 'Threshold break should be 95');
    console.log('✅ Test 1 Passed: loadThresholds loads correct values from stryker-config.json');
  });

  // Test 2: parseScoreFromDescription
  it('parseScoreFromDescription correctly extracts numeric percentage', () => {
    assert.strictEqual(parseScoreFromDescription('Stryker: 100% (240/240 killed) - ✅ HIGH'), 100);
    assert.strictEqual(parseScoreFromDescription('Stryker: 98.5% (200/203 killed) - 🟡 LOW'), 98.5);
    assert.strictEqual(parseScoreFromDescription('Stryker: 95.0% - 🟠 WARNING'), 95.0);
    assert.strictEqual(parseScoreFromDescription('Stryker: 94.2% - ❌ FAILED'), 94.2);
    assert.strictEqual(parseScoreFromDescription(null), null);
    assert.strictEqual(parseScoreFromDescription('No percentage here'), null);
    console.log('✅ Test 2 Passed: parseScoreFromDescription correctly extracts numeric percentage');
  });

  // Test 3: evaluateScore
  it('evaluateScore correctly categorizes scores and break gate', () => {
    const thresholds = { high: 100, low: 98, break: 95 };

    const resHigh = evaluateScore(100, thresholds);
    assert.strictEqual(resHigh.status, '✅ HIGH');
    assert.strictEqual(resHigh.passedBreak, true);

    const resLow = evaluateScore(98.5, thresholds);
    assert.strictEqual(resLow.status, '🟡 LOW');
    assert.strictEqual(resLow.passedBreak, true);

    const resWarn = evaluateScore(96.0, thresholds);
    assert.strictEqual(resWarn.status, '🟠 WARNING');
    assert.strictEqual(resWarn.passedBreak, true);

    const resBreakExact = evaluateScore(95.0, thresholds);
    assert.strictEqual(resBreakExact.status, '🟠 WARNING');
    assert.strictEqual(resBreakExact.passedBreak, true);

    const resFail = evaluateScore(94.9, thresholds);
    assert.strictEqual(resFail.status, '❌ FAILED');
    assert.strictEqual(resFail.passedBreak, false);

    console.log('✅ Test 3 Passed: evaluateScore correctly categorizes scores and break gate');
  });
});

(async () => {
  // Test 4: verifyMutationGate with valid fresh evidence on main
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'dotnet-messaging' },
      sha: 'fresh1234567890'
    };

    const freshDate = new Date().toISOString();

    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'fresh1234567890' }]
          }),
          getCombinedStatusForRef: async ({ ref }) => {
            if (ref === 'fresh1234567890') {
              return {
                data: {
                  statuses: [
                    {
                      context: 'mutation-testing/stryker',
                      state: 'success',
                      description: 'Score: 100.0% (8/8 packages >= 95%) - ✅ HIGH',
                      updated_at: freshDate,
                      target_url: 'https://github.com/ericksonlopezf/dotnet-messaging/actions/runs/12345'
                    }
                  ]
                }
              };
            }
            return { data: { statuses: [] } };
          }
        }
      }
    };

    const outputs = {};
    const mockCore = {
      setOutput: (k, v) => { outputs[k] = v; }
    };

    const res = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(res.needsStryker, false, 'Should not need Stryker when valid evidence exists');
    assert.strictEqual(res.canProceed, true, 'Should allow publication without re-running Stryker');
    assert.strictEqual(outputs.needs_stryker, 'false');
    assert.strictEqual(outputs.can_proceed, 'true');
    console.log('✅ Test 4 Passed: verifyMutationGate reuses valid fresh Stryker evidence on main');
  }

  // Test 5: Condition 1 - No prior run on main -> needs_stryker = true
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'dotnet-messaging' },
      sha: 'notfound123'
    };

    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({ data: [] }),
        },
        actions: {
          listWorkflowRuns: async () => ({ data: { workflow_runs: [] } })
        }
      }
    };

    const outputs = {};
    const mockCore = {
      setOutput: (k, v) => { outputs[k] = v; }
    };

    const res = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(res.needsStryker, true, 'Condition 1: Must trigger Stryker when no evidence is found');
    assert.strictEqual(res.canProceed, false);
    assert.strictEqual(outputs.needs_stryker, 'true');
    assert.strictEqual(outputs.can_proceed, 'false');
    console.log('✅ Test 5 Passed: Condition 1 (No Prior Run) triggers needs_stryker');
  }

  // Test 6: Condition 2 - Expired report (> 7 days TTL) -> needs_stryker = true
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'dotnet-messaging' },
      sha: 'expired123'
    };

    // 26 days old (incident scenario)
    const oldDate = new Date(Date.now() - 26 * 24 * 60 * 60 * 1000).toISOString();

    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'expired123' }]
          }),
          getCombinedStatusForRef: async () => ({
            data: {
              statuses: [
                {
                  context: 'mutation-testing/stryker',
                  state: 'success',
                  description: 'Score: 100.0% - ✅ HIGH',
                  updated_at: oldDate,
                  target_url: 'https://github.com/ericksonlopezf/dotnet-messaging/actions/runs/999'
                }
              ]
            }
          })
        }
      }
    };

    const outputs = {};
    const mockCore = {
      setOutput: (k, v) => { outputs[k] = v; }
    };

    const res = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(res.needsStryker, true, 'Condition 2: Expired report must trigger Stryker');
    assert.strictEqual(res.canProceed, false);
    assert.strictEqual(outputs.needs_stryker, 'true');
    assert.strictEqual(outputs.can_proceed, 'false');
    console.log('✅ Test 6 Passed: Condition 2 (TTL Expired) triggers needs_stryker');
  }

  // Test 7: Condition 3 - Production code drift in src/ -> needs_stryker = true
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'dotnet-messaging' },
      sha: 'targetSha789'
    };

    const freshDate = new Date().toISOString();

    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'baseSha123' }]
          }),
          getCombinedStatusForRef: async () => ({
            data: {
              statuses: [
                {
                  context: 'mutation-testing/stryker',
                  state: 'success',
                  description: 'Score: 100.0% - ✅ HIGH',
                  updated_at: freshDate,
                  target_url: 'https://github.com/ericksonlopezf/dotnet-messaging/actions/runs/1000'
                }
              ]
            }
          }),
          compareCommits: async () => ({
            data: {
              files: [
                { filename: 'src/EricksonLopez.SharedKernel/Domain/Entity.cs' },
                { filename: 'README.md' }
              ]
            }
          })
        }
      }
    };

    const outputs = {};
    const mockCore = {
      setOutput: (k, v) => { outputs[k] = v; }
    };

    const res = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(res.needsStryker, true, 'Condition 3: Code drift in src/ must trigger Stryker');
    assert.strictEqual(res.canProceed, false);
    assert.strictEqual(outputs.needs_stryker, 'true');
    console.log('✅ Test 7 Passed: Condition 3 (Production Code Drift) triggers needs_stryker');
  }

  // Test 8: Condition 4 - Prior run below break threshold (< 95%) -> needs_stryker = true
  {
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'dotnet-messaging' },
      sha: 'failedScore123'
    };

    const freshDate = new Date().toISOString();

    const mockGithub = {
      rest: {
        repos: {
          listCommits: async () => ({
            data: [{ sha: 'failedScore123' }]
          }),
          getCombinedStatusForRef: async () => ({
            data: {
              statuses: [
                {
                  context: 'mutation-testing/stryker',
                  state: 'failure',
                  description: 'Score: 92.4% (< 95%) - ❌ FAILED',
                  updated_at: freshDate,
                  target_url: 'https://github.com/ericksonlopezf/dotnet-messaging/actions/runs/1001'
                }
              ]
            }
          })
        }
      }
    };

    const outputs = {};
    const mockCore = {
      setOutput: (k, v) => { outputs[k] = v; }
    };

    const res = await verifyMutationGate({ github: mockGithub, context: mockContext, core: mockCore });
    assert.strictEqual(res.needsStryker, true, 'Condition 4: Failed threshold must trigger Stryker');
    assert.strictEqual(res.canProceed, false);
    assert.strictEqual(outputs.needs_stryker, 'true');
    console.log('✅ Test 8 Passed: Condition 4 (Score Regression / Gate Failure) triggers needs_stryker');
  }

  // Test 9: skip_mutation_gate emergency bypass
  {
    process.env.SKIP_MUTATION_GATE = 'true';
    const mockContext = {
      repo: { owner: 'ericksonlopezf', repo: 'dotnet-messaging' },
      sha: 'bypass123'
    };

    const outputs = {};
    const mockCore = {
      setOutput: (k, v) => { outputs[k] = v; }
    };

    const res = await verifyMutationGate({ github: {}, context: mockContext, core: mockCore });
    assert.strictEqual(res.needsStryker, false);
    assert.strictEqual(res.canProceed, true);
    assert.strictEqual(res.bypassed, true);
    delete process.env.SKIP_MUTATION_GATE;
    console.log('✅ Test 9 Passed: skip_mutation_gate bypass functions correctly');
  }

  console.log('\n🎉 ALL 9 QUALITY GATE TESTS PASSED!\n');
})();
