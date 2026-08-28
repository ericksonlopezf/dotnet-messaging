// Copyright © Erickson Lopez. MIT License.
const { spawnSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const packages = [
  { name: 'Core', config: 'stryker-config.json', outputDir: 'StrykerOutput/core' },
  { name: 'Abstractions', config: 'stryker-abstractions-config.json', outputDir: 'StrykerOutput/abstractions' },
  { name: 'Analyzers', config: 'stryker-analyzers-config.json', outputDir: 'StrykerOutput/analyzers' },
  { name: 'AzureServiceBus', config: 'stryker-azureservicebus-config.json', outputDir: 'StrykerOutput/azureservicebus' },
  { name: 'Generators', config: 'stryker-generators-config.json', outputDir: 'StrykerOutput/generators' },
  { name: 'OpenTelemetry', config: 'stryker-opentelemetry-config.json', outputDir: 'StrykerOutput/opentelemetry' },
  { name: 'RabbitMQ', config: 'stryker-rabbitmq-config.json', outputDir: 'StrykerOutput/rabbitmq' },
  { name: 'AwsSqs', config: 'stryker-awssqs-config.json', outputDir: 'StrykerOutput/awssqs' },
  { name: 'Kafka', config: 'stryker-kafka-config.json', outputDir: 'StrykerOutput/kafka' },
  { name: 'Events', config: 'stryker-events-config.json', outputDir: 'StrykerOutput/events' },
  { name: 'Testing', config: 'stryker-testing-config.json', outputDir: 'StrykerOutput/testing' },
];

function loadThresholds(configPath = 'stryker-config.json') {
  let thresholds = { high: 100, low: 98, break: 95 };
  try {
    if (fs.existsSync(configPath)) {
      const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
      const t = config['stryker-config']?.thresholds || config.thresholds || {};
      thresholds = { high: t.high ?? 100, low: t.low ?? 98, break: t.break ?? 95 };
    }
  } catch (err) {
    console.warn(`Could not parse ${configPath}: ${err.message}`);
  }
  return thresholds;
}

function runCommand(command, args) {
  console.log(`\n> ${command} ${args.join(' ')}`);
  const result = spawnSync(command, args, {
    stdio: 'inherit',
    shell: true,
  });
  return result.status ?? 1;
}

async function main() {
  console.log('============================================================');
  console.log('  EXECUTING CONDITIONAL STRYKER MUTATION SUITE');
  console.log('============================================================');

  fs.mkdirSync('StrykerOutput', { recursive: true });

  const mutationLevel = process.env.STRYKER_MUTATION_LEVEL || 'Standard';

  for (const pkg of packages) {
    console.log(`\n[STRYKER] Running mutation tests for package: ${pkg.name}...`);
    
    // Execute dotnet stryker
    runCommand('dotnet', [
      'stryker',
      '--config-file', pkg.config,
      '--test-runner', 'vstest',
      '--mutation-level', mutationLevel,
      '--output', pkg.outputDir
    ]);

    // Record result JSON and step summary
    runCommand('node', [
      'scripts/record-stryker-result.js',
      pkg.outputDir,
      pkg.name,
      pkg.config
    ]);
  }

  // Consolidated Gate Evaluation
  console.log('\n============================================================');
  console.log('  CONSOLIDATED STRYKER QUALITY GATE EVALUATION');
  console.log('============================================================');

  let totalKilled = 0;
  let totalMutants = 0;
  let allPassed = true;
  const breakThreshold = 95;
  const packageSummaries = [];

  for (const pkg of packages) {
    const summaryFile = path.join('StrykerOutput', `summary-${pkg.name}.json`);
    if (fs.existsSync(summaryFile)) {
      try {
        const data = JSON.parse(fs.readFileSync(summaryFile, 'utf8'));
        packageSummaries.push(data);
        totalKilled += data.mutants_killed || 0;
        totalMutants += data.total_mutants || 0;
        if (!data.passed_break) {
          allPassed = false;
        }
      } catch (err) {
        console.error(`Error reading ${summaryFile}:`, err.message);
        allPassed = false;
      }
    } else {
      console.warn(`[WARN] Missing summary file for ${pkg.name}`);
      allPassed = false;
    }
  }

  const overallScore = totalMutants > 0 ? Number(((totalKilled / totalMutants) * 100).toFixed(2)) : 100.0;
  let statusLabel = '❌ FAILED';
  if (overallScore >= 100) statusLabel = '✅ HIGH';
  else if (overallScore >= 98) statusLabel = '🟡 LOW';
  else if (overallScore >= breakThreshold) statusLabel = '🟠 WARNING';

  const gatePassed = allPassed && overallScore >= breakThreshold;

  console.log(`Total Mutants Killed: ${totalKilled} / ${totalMutants}`);
  console.log(`Overall Mutation Score: ${overallScore}%`);
  console.log(`Status: ${statusLabel}`);
  console.log(`Gate Verdict: ${gatePassed ? '✅ PASSED' : '❌ FAILED'}`);

  // Write Consolidated GitHub Step Summary
  const stepSummaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (stepSummaryPath) {
    let md = `\n# 🛡️ Consolidated Stryker Mutation Testing Gate\n\n`;
    md += `| Package | Score | Killed / Total | Status |\n`;
    md += `|---------|-------|----------------|--------|\n`;
    for (const p of packageSummaries) {
      md += `| **${p.package}** | **${p.mutation_score}%** | ${p.mutants_killed}/${p.total_mutants} | ${p.status} |\n`;
    }
    md += `| **OVERALL** | **${overallScore}%** | **${totalKilled}/${totalMutants}** | **${statusLabel}** |\n\n`;
    md += `**Gate Verdict**: ${gatePassed ? '✅ PASSED' : '❌ FAILED'} (Break Threshold: ≥${breakThreshold}%)\n`;
    fs.appendFileSync(stepSummaryPath, md, 'utf8');
  }

  if (!gatePassed) {
    console.error(`\n❌ STRYKER GATE FAILED: Overall mutation score (${overallScore}%) is below break threshold (${breakThreshold}%).`);
    process.exit(1);
  }

  console.log(`\n✅ STRYKER MUTATION TESTING SUITE PASSED (Score: ${overallScore}% >= ${breakThreshold}%). Proceeding.`);
  process.exit(0);
}

main().catch(err => {
  console.error('Fatal error running Stryker suite:', err);
  process.exit(1);
});
