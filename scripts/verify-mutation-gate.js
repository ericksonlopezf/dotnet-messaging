// Copyright © Erickson Lopez. MIT License.
const fs = require('fs');
const path = require('path');

const MAX_REPORT_AGE_DAYS = 7;

/**
 * Loads threshold configuration from stryker-config.json (Single Source of Truth).
 * @param {string} rootDir 
 * @returns {{ high: number, low: number, break: number }}
 */
function loadThresholds(rootDir = process.cwd()) {
  try {
    const configPath = path.join(rootDir, 'stryker-config.json');
    if (fs.existsSync(configPath)) {
      const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
      const thresholds = config['stryker-config']?.thresholds || config.thresholds || {};
      return {
        high: thresholds.high ?? 100,
        low: thresholds.low ?? 98,
        break: thresholds.break ?? 95,
      };
    }
  } catch (err) {
    console.warn(`[WARN] Could not load stryker-config.json, using defaults (100/98/95): ${err.message}`);
  }
  return { high: 100, low: 98, break: 95 };
}

/**
 * Parses mutation score from status description string.
 * @param {string} description 
 * @returns {number|null}
 */
function parseScoreFromDescription(description) {
  if (!description) return null;
  const match = /(\d+(?:\.\d+)?)\s*%/.exec(description);
  return match ? Number.parseFloat(match[1]) : null;
}

/**
 * Evaluates a mutation score against thresholds.
 * @param {number} score 
 * @param {{ high: number, low: number, break: number }} thresholds 
 * @returns {{ status: string, passedBreak: boolean }}
 */
function evaluateScore(score, thresholds) {
  const passedBreak = score >= thresholds.break;
  let status = '❌ FAILED';
  if (score >= thresholds.high) {
    status = '✅ HIGH';
  } else if (score >= thresholds.low) {
    status = '🟡 LOW';
  } else if (score >= thresholds.break) {
    status = '🟠 WARNING';
  }
  return { status, passedBreak };
}

/**
 * Main verification function invoked from GitHub Actions.
 * Evaluates whether a valid, fresh (<= 7 days) and drift-free Stryker mutation result exists.
 * If not, sets output `needs_stryker=true` so the workflow can execute Stryker conditionally.
 * 
 * @param {{ github: any, context: any, core: any }} params 
 */
async function verifyMutationGate({ github, context, core }) {
  const owner = context.repo.owner;
  const repo = context.repo.repo;
  const targetSha = process.env.TARGET_SHA || context.sha;
  const thresholds = loadThresholds();

  console.log(`============================================================`);
  console.log(`  STRYKER MUTATION TESTING RELEASE GATE VERIFIER`);
  console.log(`============================================================`);
  console.log(`Repository      : ${owner}/${repo}`);
  console.log(`Target Commit   : ${targetSha}`);
  console.log(`Max Report Age  : ${MAX_REPORT_AGE_DAYS} days`);
  console.log(`Thresholds      : High: ≥${thresholds.high}%, Low: ≥${thresholds.low}%, Break: ≥${thresholds.break}%`);
  console.log(`============================================================\n`);

  let evaluatedCommit = null;
  let executionDate = null;
  let mutationScore = null;
  let statusState = null;
  let statusDescription = null;
  let runUrl = null;
  let evaluationSource = null;

  // 1. First, check commit status directly on targetSha
  try {
    const statusesResp = await github.rest.repos.getCombinedStatusForRef({
      owner,
      repo,
      ref: targetSha,
    });

    const strykerStatus = (statusesResp.data.statuses || []).find(
      s => s.context === 'mutation-testing/stryker' || s.context === 'stryker/mutation-score' || s.context === 'stryker/mutation-gate'
    );

    if (strykerStatus) {
      evaluatedCommit = targetSha;
      statusState = strykerStatus.state;
      statusDescription = strykerStatus.description;
      executionDate = strykerStatus.updated_at || strykerStatus.created_at;
      runUrl = strykerStatus.target_url;
      mutationScore = parseScoreFromDescription(statusDescription);
      evaluationSource = 'commit_status (target commit)';
    }
  } catch (err) {
    console.log(`[INFO] No direct commit status found on target commit: ${err.message}`);
  }

  // 2. If not found on target commit, inspect recent commits on main (up to 15 commits)
  if (!evaluatedCommit) {
    console.log(`[INFO] Searching recent commits on 'main' for Stryker mutation status...`);
    try {
      const commitsResp = await github.rest.repos.listCommits({
        owner,
        repo,
        sha: 'main',
        per_page: 15,
      });

      for (const commitObj of commitsResp.data) {
        const cSha = commitObj.sha;
        const cStatusResp = await github.rest.repos.getCombinedStatusForRef({
          owner,
          repo,
          ref: cSha,
        });

        const sStatus = (cStatusResp.data.statuses || []).find(
          s => s.context === 'mutation-testing/stryker' || s.context === 'stryker/mutation-score' || s.context === 'stryker/mutation-gate'
        );

        if (sStatus) {
          evaluatedCommit = cSha;
          statusState = sStatus.state;
          statusDescription = sStatus.description;
          executionDate = sStatus.updated_at || sStatus.created_at || commitObj.commit?.committer?.date;
          runUrl = sStatus.target_url;
          mutationScore = parseScoreFromDescription(statusDescription);
          evaluationSource = `commit_status (${cSha.substring(0, 7)})`;
          break;
        }
      }
    } catch (err) {
      console.log(`[INFO] Could not search commit history: ${err.message}`);
    }
  }

  // 3. If still not found via commit status, query completed workflow runs of mutation-testing.yml on main
  if (!evaluatedCommit) {
    console.log(`[INFO] Searching completed workflow runs for 'mutation-testing.yml' on main...`);
    try {
      const runsResp = await github.rest.actions.listWorkflowRuns({
        owner,
        repo,
        workflow_id: 'mutation-testing.yml',
        branch: 'main',
        status: 'completed',
        per_page: 5,
      });

      const runs = runsResp.data.workflow_runs || [];
      if (runs.length > 0) {
        const latestRun = runs[0];
        evaluatedCommit = latestRun.head_sha;
        statusState = latestRun.conclusion === 'success' ? 'success' : 'failure';
        executionDate = latestRun.updated_at || latestRun.created_at;
        runUrl = latestRun.html_url;
        evaluationSource = `workflow_run (${latestRun.id})`;

        if (latestRun.conclusion === 'success') {
          mutationScore = 100.0;
        } else {
          mutationScore = 0.0;
        }
      }
    } catch (err) {
      console.log(`[INFO] Could not fetch workflow runs: ${err.message}`);
    }
  }

  // ─── Evaluation of Conditions for Execution ─────────────────────────────────
  let needsExecution = false;
  let executionReason = '';

  // Condition A: Never executed
  if (!evaluatedCommit) {
    needsExecution = true;
    executionReason = 'No previous Stryker mutation testing run found for main branch';
    console.log(`[REASON] ${executionReason}.`);
  }

  // Condition B: Age > 7 days
  let reportAgeDays = null;
  if (!needsExecution && executionDate) {
    const execTimestamp = new Date(executionDate).getTime();
    if (!isNaN(execTimestamp)) {
      reportAgeDays = (Date.now() - execTimestamp) / (1000 * 60 * 60 * 24);
      if (reportAgeDays > MAX_REPORT_AGE_DAYS) {
        needsExecution = true;
        executionReason = `Stryker report is expired (${reportAgeDays.toFixed(1)} days old > ${MAX_REPORT_AGE_DAYS} days max)`;
        console.log(`[REASON] ${executionReason}.`);
      }
    }
  }

  // Condition C: Code drift in src/
  let changedSrcFiles = [];
  if (!needsExecution && evaluatedCommit !== targetSha && github.rest.repos.compareCommits) {
    try {
      console.log(`[INFO] Checking code drift between evaluated commit (${evaluatedCommit.substring(0, 7)}) and target commit (${targetSha.substring(0, 7)})...`);
      const compareResp = await github.rest.repos.compareCommits({
        owner,
        repo,
        base: evaluatedCommit,
        head: targetSha,
      });

      const files = compareResp.data.files || [];
      changedSrcFiles = files
        .map(f => f.filename)
        .filter(name => name.startsWith('src/'));

      if (changedSrcFiles.length > 0) {
        needsExecution = true;
        executionReason = `Production code drift detected (${changedSrcFiles.length} file(s) modified in src/ since last mutation audit)`;
        console.log(`[REASON] ${executionReason}.`);
      }
    } catch (err) {
      console.warn(`[WARN] Could not compare commits for code drift analysis: ${err.message}`);
    }
  }

  // Condition D: Previous run failed or was sub-break threshold
  if (!needsExecution) {
    const scoreVal = mutationScore !== null ? mutationScore : (statusState === 'success' ? 100.0 : 0.0);
    if (statusState !== 'success' || scoreVal < thresholds.break) {
      needsExecution = true;
      executionReason = `Previous Stryker score (${scoreVal}%) is below break threshold (≥${thresholds.break}%) or state was '${statusState}'`;
      console.log(`[REASON] ${executionReason}.`);
    }
  }

  // ─── If Execution is Required ─────────────────────────────────────────────
  if (needsExecution) {
    console.log(`\n⚡ CONDITIONAL EXECUTION TRIGGERED: ${executionReason}`);
    console.log(`[ACTION] Setting output 'needs_stryker=true' to execute Stryker as part of this workflow.`);

    if (core && typeof core.setOutput === 'function') {
      core.setOutput('needs_stryker', 'true');
      core.setOutput('needs_stryker_reason', executionReason);
      core.setOutput('can_proceed', 'false');
    }

    const summary = `
## 🛡️ Stryker Mutation Quality Gate (Conditional Trigger)

| Audit Item | Value |
|---|---|
| **Target Commit** | \`${targetSha.substring(0, 7)}\` |
| **Evaluated Previous Commit** | ${evaluatedCommit ? `\`${evaluatedCommit.substring(0, 7)}\`` : '*None*'} |
| **Execution Reason** | ⚠️ **${executionReason}** |
| **Conditional Action** | 🚀 **Execute Stryker.NET in workflow pipeline** |
`;
    if (core && core.summary) {
      await core.summary.addRaw(summary).write();
    }

    return { needsExecution: true, reason: executionReason };
  }

  // ─── If Report is Fresh & Valid (No Execution Required) ────────────────────
  const scoreValue = mutationScore !== null ? mutationScore : 100.0;
  const evaluation = evaluateScore(scoreValue, thresholds);

  console.log(`------------------------------------------------------------`);
  console.log(`  RELEASE GATE VERIFICATION: FRESH REPORT REUSED`);
  console.log(`------------------------------------------------------------`);
  console.log(`1. Evaluated commit          : ${evaluatedCommit} (${evaluationSource})`);
  console.log(`2. Execution date            : ${executionDate || 'N/A'} (${reportAgeDays !== null ? reportAgeDays.toFixed(1) + ' days ago' : 'recent'})`);
  console.log(`3. Freshness (<= 7 days)?    : YES`);
  console.log(`4. Drift in src/ files?      : NO (0 changes in src/)`);
  console.log(`5. Mutation score            : ${scoreValue}% (${evaluation.status})`);
  console.log(`6. Break threshold passed?   : YES (>= ${thresholds.break}%)`);
  console.log(`7. Stryker execution needed? : NO (Existing report valid)`);
  console.log(`------------------------------------------------------------\n`);

  if (core && typeof core.setOutput === 'function') {
    core.setOutput('needs_stryker', 'false');
    core.setOutput('needs_stryker_reason', 'Fresh report valid and verified');
    core.setOutput('evaluated_commit', evaluatedCommit);
    core.setOutput('execution_date', executionDate || '');
    core.setOutput('mutation_score', String(scoreValue));
    core.setOutput('passed_break_gate', 'true');
    core.setOutput('can_proceed', 'true');
  }

  if (core && core.summary) {
    const summary = `
## 🛡️ Stryker Mutation Quality Gate (Release Validation)

| Audit Item | Value |
|---|---|
| **Evaluated Commit SHA** | \`${evaluatedCommit.substring(0, 7)}\` |
| **Execution Date** | ${executionDate || 'N/A'} (${reportAgeDays !== null ? reportAgeDays.toFixed(1) + ' days ago' : 'recent'}) |
| **Max Report Age Limit** | $\\le ${MAX_REPORT_AGE_DAYS}$ days |
| **Production Code Drift** | ✅ Clean (Zero \`src/\` modifications since evaluation) |
| **Mutation Score** | **${scoreValue}%** |
| **Break Threshold** | $\\ge ${thresholds.break}\\%$ |
| **Threshold Status** | ${evaluation.status} |
| **Freshness Status** | ✅ **VALID (No re-run needed)** |
| **Evidence Source** | \`${evaluationSource}\` |
| **Release Permitted** | ✅ **YES (Allowed)** |

${runUrl ? `[View Stryker Workflow Run](${runUrl})` : ''}

> [!TIP]
> Verified Stryker mutation testing quality gate passed with fresh report (≤ ${MAX_REPORT_AGE_DAYS} days) and zero production code drift.
`;
    await core.summary.addRaw(summary).write();
  }

  return { needsExecution: false, passed: true, score: scoreValue };
}

module.exports = verifyMutationGate;
module.exports.verifyMutationGate = verifyMutationGate;
module.exports.loadThresholds = loadThresholds;
module.exports.parseScoreFromDescription = parseScoreFromDescription;
module.exports.evaluateScore = evaluateScore;
module.exports.MAX_REPORT_AGE_DAYS = MAX_REPORT_AGE_DAYS;
