using System;
using System.Collections.Generic;
using System.IO;

namespace PLL.Creator.Editor
{
    internal static class PllBuildPublisher
    {
        internal static void Publish(string BundleSource, string BundleDestination, string ReportSource,
            string ReportDestination)
        {
            RejectReadOnly(BundleDestination);
            RejectReadOnly(ReportDestination);
            var Token = Guid.NewGuid().ToString("N");
            var BundleBackup = BundleDestination + ".backup-" + Token;
            var ReportBackup = ReportDestination + ".backup-" + Token;
            var BundleExisted = File.Exists(BundleDestination);
            var ReportExisted = File.Exists(ReportDestination);
            var BundlePublished = false;
            var ReportPublished = false;
            try
            {
                Publish(BundleSource, BundleDestination, BundleBackup, BundleExisted);
                BundlePublished = true;
                Publish(ReportSource, ReportDestination, ReportBackup, ReportExisted);
                ReportPublished = true;
            }
            catch (Exception PublishFailure)
            {
                var RollbackFailures = new List<Exception>();
                Restore(ReportDestination, ReportBackup, ReportPublished, ReportExisted, RollbackFailures);
                Restore(BundleDestination, BundleBackup, BundlePublished, BundleExisted, RollbackFailures);
                if (RollbackFailures.Count == 0) throw;
                RollbackFailures.Insert(0, PublishFailure);
                throw new AggregateException("PLL bundle publication and rollback both failed. Any surviving original output backups remain at '"
                    + BundleBackup + "' or '" + ReportBackup + "'.", RollbackFailures);
            }
            DeleteBackups(BundleBackup, ReportBackup);
        }

        static void RejectReadOnly(string Path)
        {
            if (File.Exists(Path) && (File.GetAttributes(Path) & FileAttributes.ReadOnly) != 0)
                throw new InvalidOperationException("Refused to replace read-only output '" + Path + "'.");
        }

        static void Publish(string Source, string Destination, string Backup, bool DestinationExists)
        {
            if (DestinationExists) File.Replace(Source, Destination, Backup);
            else File.Move(Source, Destination);
        }

        static void Restore(string Destination, string Backup, bool Published, bool DestinationExisted,
            List<Exception> Failures)
        {
            if (!Published) return;
            try
            {
                if (!DestinationExisted)
                {
                    File.Delete(Destination);
                    return;
                }
                if (File.Exists(Destination)) File.Replace(Backup, Destination, null);
                else File.Move(Backup, Destination);
            }
            catch (Exception Failure)
            {
                Failures.Add(Failure);
            }
        }

        static void DeleteBackups(params string[] Paths)
        {
            var Failures = new List<Exception>();
            foreach (var Path in Paths)
            {
                try
                {
                    if (File.Exists(Path)) File.Delete(Path);
                }
                catch (Exception Failure)
                {
                    Failures.Add(Failure);
                }
            }
            if (Failures.Count != 0)
                throw new AggregateException("PLL bundle publication succeeded, but old output backups could not be removed.", Failures);
        }
    }
}
