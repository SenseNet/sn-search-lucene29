using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.Diagnostics;
using SenseNet.Search.Lucene29;

namespace CentralizedIndexBackupTester
{
    public class CancellationTest : ValidityTest
    {
        public CancellationTest(ILuceneIndexingEngine engine) : base(engine) { }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            using (var op = SnTrace.Test.StartOperation(this.GetType().Name))
            {
                // Start an editor worker agent
                var finisher = new CancellationTokenSource();
                var _ = CreateWorker().WorkAsync(finisher.Token).ConfigureAwait(false);

                // Wait for backup
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                // Do backup async
#pragma warning disable 4014
                BackupAsync(cancellationToken);
#pragma warning restore 4014

                // Wait a bit
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);

                // Cancel th backup operation
                await _engine.CancelBackupAsync(cancellationToken);

                // Wait for finish
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                // Stop worker agent
                finisher.Cancel();

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                op.Successful = true;
            }
        }

    }
}
