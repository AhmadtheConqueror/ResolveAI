using System.Diagnostics;

namespace ResolveAI.Api.Tests;

public class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("RESOLVEAI AUTOMATED BACKEND TEST SUITE");
        Console.WriteLine("==================================================");

        var stopwatch = Stopwatch.StartNew();
        var failureCount = 0;

        if (args.Contains("--smoke-test"))
        {
            ExternalEmailDeliveryTests.RunResendSmokeTest();
            return 0;
        }

        void RunSuite(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failureCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[FAILED] {name}: {ex.Message}");
                Console.ResetColor();
            }
        }

        RunSuite("IncidentWorkflowTests", () => new IncidentWorkflowTests().RunAllTests());
        RunSuite("SlaServiceTests", () => new SlaServiceTests().RunAllTests());
        RunSuite("NotificationServiceTests", () => new NotificationServiceTests().RunAllTests());
        RunSuite("AuditTrailTests", () => new AuditTrailTests().RunAllTests());
        RunSuite("UserAdminAndAuthTests", () => new UserAdminAndAuthTests().RunAllTests());
        RunSuite("AiIncidentServiceTests", () => new AiIncidentServiceTests().RunAllTests());
        RunSuite("AIResolutionAssistantTests", () => new AIResolutionAssistantTests().RunAllTests());
        RunSuite("ExternalEmailDeliveryTests", () => new ExternalEmailDeliveryTests().RunAllTests());

        stopwatch.Stop();

        Console.WriteLine("\n==================================================");
        if (failureCount == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"TEST EXECUTION SUCCESSFUL: All test suites passed in {stopwatch.ElapsedMilliseconds}ms.");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"TEST EXECUTION FAILED: {failureCount} suite(s) had errors.");
            Console.ResetColor();
            return 1;
        }
    }
}
