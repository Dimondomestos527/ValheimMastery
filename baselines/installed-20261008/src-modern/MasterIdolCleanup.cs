using System;
using System.Collections.Generic;

namespace ValheimMastery
{
    internal static class MasterIdolCleanup
    {
        private static readonly Queue<string> Failures=new Queue<string>();
        internal static bool ReportingFailed {get;private set;}
        internal static string Describe()=>string.Join("\n",Failures);
        // Independent transient cleanup stages; failures retain their original stack and do not skip later stages.
        internal static bool Run(string stage, Action cleanup, Action<string, Exception> report)
        {
            try { cleanup(); return true; }
            catch (Exception error)
            {
                string diagnostic="[MasterIdols] Cleanup failed stage="+stage+"\n"+error;
                try { report(stage,error); }
                catch(Exception reportingError){ReportingFailed=true;diagnostic+="\nCleanup reporting failed:\n"+reportingError;}
                if(Failures.Count>=8)Failures.Dequeue();Failures.Enqueue(diagnostic);
                return false;
            }
        }
    }
}
