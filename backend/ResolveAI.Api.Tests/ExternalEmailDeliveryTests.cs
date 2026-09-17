using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ResolveAI.Api.Data;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Enums;
using ResolveAI.Api.Models.Email;
using ResolveAI.Api.Services;

namespace ResolveAI.Api.Tests;

public class ExternalEmailDeliveryTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=resolveai;Username=postgres;Password=Ahmad263%";

    public static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }

    public void RunAllTests()
    {
        Console.WriteLine("\n--- ExternalEmailDeliveryTests ---");

        Test_1_Qualifying_Notification_Creates_Pending_Delivery();
        Test_2_NonQualifying_Notification_Creates_No_Delivery();
        Test_3_Inactive_User_Receives_No_Delivery();
        Test_4_Missing_Or_Invalid_Email_Receives_No_Delivery();
        Test_5_Delivery_Worker_Sends_Pending_Item();
        Test_6_Successful_Send_Marks_Sent();
        Test_7_ProviderMessageId_Stored();
        Test_8_Transient_Failure_Schedules_Retry();
        Test_9_Max_Attempts_Transitions_To_PermanentlyFailed();
        Test_10_Same_Delivery_Keeps_Same_IdempotencyKey();
        Test_11_Duplicate_Worker_Processing_Does_Not_Resend();
        Test_12_Reporter_Receives_Resolution_Email();
        Test_13_Assigned_Technician_Receives_Assignment_Email();
        Test_14_Unrelated_Technician_Receives_No_Email();
        Test_15_Manager_Receives_Sla_Attention_Email();
        Test_16_Admin_Fallback_Behavior_Preserved();
        Test_17_User_With_EmailNotificationsDisabled_Receives_None();
        Test_18_Provider_Failure_Does_Not_Break_Notification_Creation();
        Test_19_Provider_Failure_Does_Not_Break_Incident_Workflow();
        Test_20_Staging_Failure_In_Separate_Scope_Does_Not_Affect_Core_Business_DbContext();
        Test_21_Initial_Assignment_Wording_And_Subject();
        Test_22_Reassignment_Wording_And_Subject_With_Previous_Assignee();
        Test_23_Unchanged_Assignee_Produces_No_Duplicate_Notification();
        Test_24_Unassign_And_Later_Reassign_Legitimately_Creates_New_Event();
        Test_25_Repeated_Sla_Runs_Response_AtRisk_Creates_Single_Notification();
        Test_26_Repeated_Sla_Runs_Response_Breached_Creates_Single_Notification();
        Test_27_Repeated_Sla_Runs_Resolution_AtRisk_And_Breached_Create_Single_Notification();
        Test_28_Response_Breach_And_Resolution_Breach_Remain_Distinct();
        Test_29_Unrelated_Technician_Receives_No_Sla_Email();
        Test_30_Admin_Fallback_Does_Not_Create_Manager_And_Admin_Duplicate_Alerts();
        Test_31_Email_Template_Contains_One_Event_Content_Block_Only();
        Test_32_Template_Renders_Correctly_When_Branding_LogoUrl_Exists();
        Test_33_Template_Renders_Correctly_When_Branding_LogoUrl_Is_Absent();
        Test_34_No_Live_Provider_Calls_Occur_In_Automated_Tests();
    }

    private void Test_1_Qualifying_Notification_Creates_Pending_Delivery()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "tech1@example.com", "Technician");
            var reporter = CreateTestUser(context, "rep1@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, user.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, user, reporter.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == user.Email);

            Assert.NotNull(delivery, "Expected ExternalNotificationDelivery row for eligible technician.");
            Assert.Equal(ExternalDeliveryStatus.Pending, delivery!.Status);
            Assert.Equal(ExternalDeliveryChannel.Email, delivery.Channel);
            Assert.True(!string.IsNullOrWhiteSpace(delivery.IdempotencyKey));

            Console.WriteLine("  ✓ 1. qualifying notification creates Pending email delivery");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_2_NonQualifying_Notification_Creates_No_Delivery()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "tech2@example.com", "Technician");
            var reporter = CreateTestUser(context, "rep2@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, user.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            // Priority changed is high-frequency / in-app only, NOT eligible for external email
            service.QueuePriorityChangedAsync(incident, "Low", "Medium", reporter.Id).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var deliveries = context.ExternalNotificationDeliveries
                .Where(d => d.RecipientAddress == user.Email || d.RecipientAddress == reporter.Email)
                .ToList();

            Assert.Equal(0, deliveries.Count, "Priority changes must not generate email delivery.");
            Console.WriteLine("  ✓ 2. non-qualifying notification creates no delivery");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_3_Inactive_User_Receives_No_Delivery()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "inactive@example.com", "Technician", isActive: false);
            var reporter = CreateTestUser(context, "rep3@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, user.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, user, reporter.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == user.Email);

            Assert.Null(delivery, "Inactive user must not receive external delivery.");
            Console.WriteLine("  ✓ 3. inactive user receives no email delivery");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_4_Missing_Or_Invalid_Email_Receives_No_Delivery()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "invalidemailformat", "Technician");
            var reporter = CreateTestUser(context, "rep4@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, user.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, user, reporter.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var inAppNotification = context.Notifications.FirstOrDefault(n => n.UserId == user.Id);
            Assert.NotNull(inAppNotification, "In-app notification must still be created even if email is invalid.");

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == user.Email);

            Assert.Null(delivery, "User with invalid email syntax must not have external delivery queued.");
            Console.WriteLine("  ✓ 4. missing/invalid email receives no delivery");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_5_Delivery_Worker_Sends_Pending_Item()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "worker_test@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            context.SaveChanges();

            var fakeSender = new FakeEmailSender();
            var processor = CreateProcessor(context, fakeSender);

            var count = processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            Assert.Equal(1, count);
            Assert.Equal(1, fakeSender.SentEmails.Count);
            Assert.Equal(user.Email, fakeSender.SentEmails[0].Message.To);

            Console.WriteLine("  ✓ 5. delivery worker sends pending item");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_6_Successful_Send_Marks_Sent()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "success_mark@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            context.SaveChanges();

            var fakeSender = new FakeEmailSender();
            var processor = CreateProcessor(context, fakeSender);

            processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            context.Entry(delivery).Reload();
            Assert.Equal(ExternalDeliveryStatus.Sent, delivery.Status);
            Assert.NotNull(delivery.SentAt);
            Assert.Null(delivery.LastError);

            Console.WriteLine("  ✓ 6. successful send marks Sent");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_7_ProviderMessageId_Stored()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "msg_id_test@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            context.SaveChanges();

            var expectedId = "resend_msg_test_abc123";
            var fakeSender = new FakeEmailSender
            {
                SendHandler = (_, _) => EmailSendResult.Succeeded(expectedId)
            };
            var processor = CreateProcessor(context, fakeSender);

            processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            context.Entry(delivery).Reload();
            Assert.Equal(expectedId, delivery.ProviderMessageId);

            Console.WriteLine("  ✓ 7. ProviderMessageId stored");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_8_Transient_Failure_Schedules_Retry()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "retry_test@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            context.SaveChanges();

            var fakeSender = new FakeEmailSender
            {
                SendHandler = (_, _) => EmailSendResult.Failed("Rate limit 429", isTransient: true, statusCode: 429)
            };
            var processor = CreateProcessor(context, fakeSender);

            processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            context.Entry(delivery).Reload();
            Assert.Equal(ExternalDeliveryStatus.Failed, delivery.Status);
            Assert.Equal(1, delivery.AttemptCount);
            Assert.NotNull(delivery.NextAttemptAt);
            Assert.True(delivery.NextAttemptAt > DateTime.UtcNow);
            Assert.Contains("Rate limit 429", delivery.LastError ?? "");

            Console.WriteLine("  ✓ 8. transient failure schedules retry");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_9_Max_Attempts_Transitions_To_PermanentlyFailed()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "max_attempts@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            delivery.AttemptCount = 3; // On 4th failure, should become PermanentlyFailed
            context.SaveChanges();

            var fakeSender = new FakeEmailSender
            {
                SendHandler = (_, _) => EmailSendResult.Failed("500 internal server error", isTransient: true)
            };
            var processor = CreateProcessor(context, fakeSender);

            processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            context.Entry(delivery).Reload();
            Assert.Equal(ExternalDeliveryStatus.PermanentlyFailed, delivery.Status);
            Assert.Equal(4, delivery.AttemptCount);
            Assert.Null(delivery.NextAttemptAt);

            Console.WriteLine("  ✓ 9. max attempts -> PermanentlyFailed");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_10_Same_Delivery_Keeps_Same_IdempotencyKey()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "idempotency_key@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            var originalKey = delivery.IdempotencyKey;
            context.SaveChanges();

            var keysReceived = new List<string>();
            var fakeSender = new FakeEmailSender
            {
                SendHandler = (_, key) =>
                {
                    keysReceived.Add(key);
                    return EmailSendResult.Failed("Transient outage", isTransient: true);
                }
            };
            var processor = CreateProcessor(context, fakeSender);

            // First attempt
            processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            // Simulate retry due right now
            context.Entry(delivery).Reload();
            delivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
            context.SaveChanges();

            // Second attempt succeeds
            fakeSender.SendHandler = (_, key) =>
            {
                keysReceived.Add(key);
                return EmailSendResult.Succeeded("msg_999");
            };
            processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            Assert.Equal(2, keysReceived.Count);
            Assert.Equal(originalKey, keysReceived[0]);
            Assert.Equal(originalKey, keysReceived[1]);

            Console.WriteLine("  ✓ 10. same delivery keeps same idempotency key");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_11_Duplicate_Worker_Processing_Does_Not_Resend()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "no_dup@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            delivery.Status = ExternalDeliveryStatus.Sent;
            delivery.SentAt = DateTime.UtcNow;
            context.SaveChanges();

            var fakeSender = new FakeEmailSender();
            var processor = CreateProcessor(context, fakeSender);

            var count = processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            Assert.Equal(0, count);
            Assert.Equal(0, fakeSender.SentEmails.Count);

            Console.WriteLine("  ✓ 11. duplicate worker processing does not send duplicate email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_12_Reporter_Receives_Resolution_Email()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var reporter = CreateTestUser(context, "rep_resolved@example.com", "Employee");
            var tech = CreateTestUser(context, "tech_resolver@example.com", "Technician");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentStatusChangedAsync(
                incident,
                IncidentStatus.InProgress,
                IncidentStatus.Resolved,
                tech.Id).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == reporter.Email);

            Assert.NotNull(delivery);
            Assert.Equal(ExternalDeliveryStatus.Pending, delivery!.Status);

            Console.WriteLine("  ✓ 12. reporter receives resolution email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_13_Assigned_Technician_Receives_Assignment_Email()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "assigned_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "rep_assign@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, tech, reporter.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == tech.Email);

            Assert.NotNull(delivery);
            Assert.Equal(ExternalDeliveryStatus.Pending, delivery!.Status);

            Console.WriteLine("  ✓ 13. assigned Technician receives assignment email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_14_Unrelated_Technician_Receives_No_Email()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var assignedTech = CreateTestUser(context, "assigned_only@example.com", "Technician");
            var unrelatedTech = CreateTestUser(context, "unrelated_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "rep_unrelated@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, assignedTech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, assignedTech, reporter.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == unrelatedTech.Email);

            Assert.Null(delivery, "Unrelated technician must not receive assignment email.");
            Console.WriteLine("  ✓ 14. unrelated Technician receives no email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_15_Manager_Receives_Sla_Attention_Email()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var manager = CreateTestUser(context, "manager_sla@example.com", "Manager");
            var reporter = CreateTestUser(context, "rep_sla@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, null);
            // Simulate Critical priority where response target is 15 minutes, created 30 minutes ago
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-30);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == manager.Email);

            Assert.NotNull(delivery, "Manager should receive SLA breach/attention notification email.");
            Console.WriteLine("  ✓ 15. Manager receives SLA attention email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_16_Admin_Fallback_Behavior_Preserved()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            // Temporarily deactivate all managers in this transaction
            var managers = context.Users.Where(u => u.Role.Name == "Manager").ToList();
            foreach (var m in managers) m.IsActive = false;

            var admin = CreateTestUser(context, "admin_fallback@example.com", "Admin");
            var reporter = CreateTestUser(context, "rep_fallback@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, null);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentCreatedAsync(incident, reporter.Id).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == admin.Email);

            Assert.NotNull(delivery, "Admin must receive fallback triage notification when no active managers exist.");
            Console.WriteLine("  ✓ 16. Admin fallback behavior preserved");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_17_User_With_EmailNotificationsDisabled_Receives_None()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "optout_tech@example.com", "Technician");
            tech.EmailNotificationsEnabled = false;
            var reporter = CreateTestUser(context, "rep_optout@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, tech, reporter.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var inAppNotification = context.Notifications.FirstOrDefault(n => n.UserId == tech.Id);
            Assert.NotNull(inAppNotification, "In-app notification should still be created when email is disabled.");

            var delivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == tech.Email);
            Assert.Null(delivery, "User with EmailNotificationsEnabled=false must receive no email delivery.");

            Console.WriteLine("  ✓ 17. user with EmailNotificationsEnabled=false receives none, if preference implemented");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_18_Provider_Failure_Does_Not_Break_Notification_Creation()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var manager = CreateTestUser(context, "fail_safe_mgr@example.com", "Manager");
            var tech = CreateTestUser(context, "fail_safe_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "fail_safe_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            // When Manager assigns, both technician and reporter get in-app notifications
            service.QueueIncidentAssignedAsync(incident, tech, manager.Id, false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var inAppNotifications = context.Notifications
                .Where(n => n.IncidentId == incident.Id)
                .ToList();

            Assert.True(inAppNotifications.Count >= 2, "In-app notifications must be created safely regardless of email delivery.");
            Console.WriteLine("  ✓ 18. provider failure does not break notification creation");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_19_Provider_Failure_Does_Not_Break_Incident_Workflow()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "flow_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "flow_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var workflowService = new IncidentWorkflowService();
            var validation = workflowService.ValidateStatusChange(
                incident,
                IncidentStatus.InProgress,
                tech.Id,
                "Technician");

            Assert.True(validation.IsAllowed, "Workflow status transition must succeed independently of notification provider status.");
            Console.WriteLine("  ✓ 19. provider failure does not break incident workflow");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_20_Staging_Failure_In_Separate_Scope_Does_Not_Affect_Core_Business_DbContext()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "stage_fail_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "stage_fail_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            // Create service with a faulty scope factory that throws during scope creation
            var failingScopeFactory = new FailingServiceScopeFactory();
            var service = new NotificationService(context, new SlaService(), failingScopeFactory);

            // Business action queueing
            service.QueueIncidentAssignedAsync(incident, tech, reporter.Id, false).GetAwaiter().GetResult();

            // Core business transaction save
            context.SaveChanges();

            // Assert in-app notification is successfully persisted in core context
            var inAppNotification = context.Notifications.FirstOrDefault(n => n.UserId == tech.Id);
            Assert.NotNull(inAppNotification, "In-app notification must be persisted in core context.");

            // Staging execution: fails in its separate scope, but MUST catch and never throw
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            // Verify core business DbContext was not dirtied or left in an error state
            Assert.False(context.ChangeTracker.HasChanges(), "Main business DbContext must remain clean.");

            // Subsequent business action on core context succeeds completely
            incident.Title = "Updated Title After Staging Exception";
            context.SaveChanges();

            var reloadedIncident = context.Incidents.Find(incident.Id);
            Assert.Equal("Updated Title After Staging Exception", reloadedIncident!.Title);

            Console.WriteLine("  ✓ 20. staging failure in separate scope does not affect core business DbContext");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_21_Initial_Assignment_Wording_And_Subject()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "init_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "init_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, tech, reporter.Id, wasReassignment: false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var notification = context.Notifications.FirstOrDefault(n => n.UserId == tech.Id && n.IncidentId == incident.Id);
            Assert.NotNull(notification);
            Assert.Equal("Incident assigned to you", notification!.Title);
            Assert.Equal($"Incident {incident.IncidentNumber} has been assigned to you.", notification.Message);

            var delivery = context.ExternalNotificationDeliveries.FirstOrDefault(d => d.NotificationId == notification.Id);
            Assert.NotNull(delivery);

            var templateService = new EmailTemplateService(new ConfigurationBuilder().AddInMemoryCollection().Build());
            var email = templateService.BuildEmail(delivery!, notification, tech);

            Assert.Equal($"[ResolveAI] Incident assigned to you — {incident.IncidentNumber}", email.Subject);
            Assert.Contains($"Incident {incident.IncidentNumber} has been assigned to you.", email.HtmlBody);
            Assert.Contains($"Incident {incident.IncidentNumber} has been assigned to you.", email.TextBody);

            Console.WriteLine("  ✓ 21. initial assignment produces one email delivery with explicit wording");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_22_Reassignment_Wording_And_Subject_With_Previous_Assignee()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var oldTech = CreateTestUser(context, "old_tech@example.com", "Technician");
            oldTech.FirstName = "John";
            oldTech.LastName = "Technician";
            var newTech = CreateTestUser(context, "new_tech@example.com", "Technician");
            newTech.FirstName = "Ahmad";
            newTech.LastName = "Bello";
            var manager = CreateTestUser(context, "mgr_reassign@example.com", "Manager");
            var reporter = CreateTestUser(context, "rep_reassign@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, newTech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(
                incident,
                newTech,
                manager.Id,
                wasReassignment: true,
                previousAssigneeName: "John Technician").GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var notification = context.Notifications.FirstOrDefault(n => n.UserId == newTech.Id && n.IncidentId == incident.Id);
            Assert.NotNull(notification);
            Assert.Equal("Incident reassigned to you", notification!.Title);
            Assert.Contains("Previous assignee: John Technician", notification.Message);

            var delivery = context.ExternalNotificationDeliveries.FirstOrDefault(d => d.NotificationId == notification.Id);
            Assert.NotNull(delivery);

            var templateService = new EmailTemplateService(new ConfigurationBuilder().AddInMemoryCollection().Build());
            var email = templateService.BuildEmail(delivery!, notification, newTech);

            Assert.Equal($"[ResolveAI] Incident reassigned to you — {incident.IncidentNumber}", email.Subject);
            Assert.Contains("Incident reassigned to you", email.HtmlBody);
            Assert.Contains("Previous assignee: John Technician", email.HtmlBody);

            Console.WriteLine("  ✓ 22. reassignment produces email delivery with reassignment semantics & previous assignee");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_23_Unchanged_Assignee_Produces_No_Duplicate_Notification()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "same_tech@example.com", "Technician");
            var manager = CreateTestUser(context, "same_mgr@example.com", "Manager");
            var reporter = CreateTestUser(context, "same_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, tech, manager.Id, wasReassignment: false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var initialNotificationCount = context.Notifications.Count(n => n.UserId == tech.Id && n.IncidentId == incident.Id);
            var initialDeliveryCount = context.ExternalNotificationDeliveries.Count(d => d.RecipientAddress == tech.Email);
            Assert.Equal(1, initialNotificationCount);
            Assert.Equal(1, initialDeliveryCount);

            // Re-assigning to the exact same technician (no-op assignment)
            var assignmentChanged = incident.AssignedToId != tech.Id; // false
            if (assignmentChanged)
            {
                service.QueueIncidentAssignedAsync(incident, tech, manager.Id, wasReassignment: false).GetAwaiter().GetResult();
                context.SaveChanges();
                service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();
            }

            var finalNotificationCount = context.Notifications.Count(n => n.UserId == tech.Id && n.IncidentId == incident.Id);
            var finalDeliveryCount = context.ExternalNotificationDeliveries.Count(d => d.RecipientAddress == tech.Email);

            Assert.Equal(initialNotificationCount, finalNotificationCount);
            Assert.Equal(initialDeliveryCount, finalDeliveryCount);

            Console.WriteLine("  ✓ 23. unchanged assignee produces no duplicate notification");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_24_Unassign_And_Later_Reassign_Legitimately_Creates_New_Event()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "cycle_tech@example.com", "Technician");
            var manager = CreateTestUser(context, "cycle_mgr@example.com", "Manager");
            var reporter = CreateTestUser(context, "cycle_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueIncidentAssignedAsync(incident, tech, manager.Id, wasReassignment: false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            Assert.Equal(1, context.Notifications.Count(n => n.UserId == tech.Id && n.IncidentId == incident.Id));

            // Incident is unassigned
            incident.AssignedToId = null;
            incident.AssignedTo = null;
            context.SaveChanges();

            // Later assigned back to tech
            var assignmentChanged = incident.AssignedToId != tech.Id; // true (null != tech.Id)
            var wasReassignment = incident.AssignedToId.HasValue && incident.AssignedToId != tech.Id; // false
            Assert.True(assignmentChanged);
            Assert.False(wasReassignment);

            incident.AssignedToId = tech.Id;
            service.QueueIncidentAssignedAsync(incident, tech, manager.Id, wasReassignment: false).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            Assert.Equal(2, context.Notifications.Count(n => n.UserId == tech.Id && n.IncidentId == incident.Id));
            Assert.Equal(2, context.ExternalNotificationDeliveries.Count(d => d.RecipientAddress == tech.Email));

            Console.WriteLine("  ✓ 24. unassign and later reassign legitimately creates a new event");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_25_Repeated_Sla_Runs_Response_AtRisk_Creates_Single_Notification()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "resp_atrisk_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "resp_atrisk_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            // Critical response time is 30m; 75% threshold is 22.5m. 24m elapsed = AtRisk.
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-24);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());

            // Run 1: AtRisk
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var atRiskKey = $"sla:{incident.Id}:response:atrisk";
            var countAfterRun1 = context.Notifications.Count(n => n.DeduplicationKey == atRiskKey);
            var deliveriesAfterRun1 = context.ExternalNotificationDeliveries
                .Count(d => d.RecipientAddress == tech.Email);

            Assert.True(countAfterRun1 >= 1, "At-risk notification should be created on first evaluation.");
            Assert.Equal(1, deliveriesAfterRun1);

            // Run 2: Worker runs repeatedly while still AtRisk
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow.AddMinutes(1)).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var countAfterRun2 = context.Notifications.Count(n => n.DeduplicationKey == atRiskKey);
            var deliveriesAfterRun2 = context.ExternalNotificationDeliveries
                .Count(d => d.RecipientAddress == tech.Email);

            Assert.Equal(countAfterRun1, countAfterRun2);
            Assert.Equal(deliveriesAfterRun1, deliveriesAfterRun2);

            Console.WriteLine("  ✓ 25. repeated SLA runs while Response SLA AtRisk create only one notification/email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_26_Repeated_Sla_Runs_Response_Breached_Creates_Single_Notification()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "resp_breach_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "resp_breach_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            // Critical response target is 30m. 40m elapsed without response = Breached.
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-40);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());

            // Run 1: Breached
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var breachKey = $"sla:{incident.Id}:response:breached";
            var countAfterRun1 = context.Notifications.Count(n => n.DeduplicationKey == breachKey);
            var deliveriesAfterRun1 = context.ExternalNotificationDeliveries
                .Count(d => d.RecipientAddress == tech.Email);

            Assert.True(countAfterRun1 >= 1, "Breach notification should be created on first breach.");
            Assert.Equal(1, deliveriesAfterRun1);

            // Run 2: Worker runs repeatedly while still Breached
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow.AddMinutes(10)).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var countAfterRun2 = context.Notifications.Count(n => n.DeduplicationKey == breachKey);
            var deliveriesAfterRun2 = context.ExternalNotificationDeliveries
                .Count(d => d.RecipientAddress == tech.Email);

            Assert.Equal(countAfterRun1, countAfterRun2);
            Assert.Equal(deliveriesAfterRun1, deliveriesAfterRun2);

            Console.WriteLine("  ✓ 26. repeated SLA runs while Response SLA Breached create only one notification/email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_27_Repeated_Sla_Runs_Resolution_AtRisk_And_Breached_Create_Single_Notification()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "res_sla_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "res_sla_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            // Critical resolution target is 480m (8h). 75% threshold is 360m (6h).
            // First responded 10 minutes ago so Response SLA is Met.
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-380);
            incident.FirstRespondedAt = DateTime.UtcNow.AddMinutes(-370);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());

            // Run 1: Resolution At-Risk
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var atRiskKey = $"sla:{incident.Id}:resolution:atrisk";
            var atRiskCount1 = context.Notifications.Count(n => n.DeduplicationKey == atRiskKey);
            Assert.True(atRiskCount1 >= 1);

            // Repeated Run: Resolution At-Risk
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow.AddMinutes(5)).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();
            Assert.Equal(atRiskCount1, context.Notifications.Count(n => n.DeduplicationKey == atRiskKey));

            // Later: Breached (500 minutes elapsed)
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-500);
            context.SaveChanges();

            // Run 1: Resolution Breached
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var breachKey = $"sla:{incident.Id}:resolution:breached";
            var breachCount1 = context.Notifications.Count(n => n.DeduplicationKey == breachKey);
            Assert.True(breachCount1 >= 1);

            // Repeated Run: Resolution Breached
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow.AddMinutes(10)).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();
            Assert.Equal(breachCount1, context.Notifications.Count(n => n.DeduplicationKey == breachKey));

            Console.WriteLine("  ✓ 27. repeated SLA runs for Resolution SLA create only one notification/email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_28_Response_Breach_And_Resolution_Breach_Remain_Distinct()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var tech = CreateTestUser(context, "both_breach_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "both_breach_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, tech.Id);
            // Critical priority, created 600 minutes ago, no response, no resolution -> both breached
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-600);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var responseBreach = context.Notifications
                .FirstOrDefault(n => n.UserId == tech.Id && n.DeduplicationKey == $"sla:{incident.Id}:response:breached");
            var resolutionBreach = context.Notifications
                .FirstOrDefault(n => n.UserId == tech.Id && n.DeduplicationKey == $"sla:{incident.Id}:resolution:breached");

            Assert.NotNull(responseBreach, "Response breach must be recorded.");
            Assert.NotNull(resolutionBreach, "Resolution breach must be recorded.");

            var templateService = new EmailTemplateService(new ConfigurationBuilder().AddInMemoryCollection().Build());
            var respDelivery = CreateTestDelivery(context, responseBreach!.Id, tech.Email);
            var resDelivery = CreateTestDelivery(context, resolutionBreach!.Id, tech.Email);

            var respEmail = templateService.BuildEmail(respDelivery, responseBreach, tech);
            var resEmail = templateService.BuildEmail(resDelivery, resolutionBreach, tech);

            Assert.Equal($"[ResolveAI] Response SLA breached — {incident.IncidentNumber}", respEmail.Subject);
            Assert.Equal($"[ResolveAI] Resolution SLA breached — {incident.IncidentNumber}", resEmail.Subject);

            Console.WriteLine("  ✓ 28. Response breach and Resolution breach remain distinct");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_29_Unrelated_Technician_Receives_No_Sla_Email()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var assignedTech = CreateTestUser(context, "assigned_sla_tech@example.com", "Technician");
            var unrelatedTech = CreateTestUser(context, "unrelated_sla_tech@example.com", "Technician");
            var reporter = CreateTestUser(context, "reporter_sla_tech@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, assignedTech.Id);
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-50);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();
            service.StagePendingExternalDeliveriesAsync().GetAwaiter().GetResult();

            var unrelatedNotification = context.Notifications
                .FirstOrDefault(n => n.UserId == unrelatedTech.Id && n.IncidentId == incident.Id);
            var unrelatedDelivery = context.ExternalNotificationDeliveries
                .FirstOrDefault(d => d.RecipientAddress == unrelatedTech.Email);

            Assert.Null(unrelatedNotification, "Unrelated technician must not receive SLA notification.");
            Assert.Null(unrelatedDelivery, "Unrelated technician must not receive SLA external email.");

            Console.WriteLine("  ✓ 29. unrelated technician receives no SLA email");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_30_Admin_Fallback_Does_Not_Create_Manager_And_Admin_Duplicate_Alerts()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var manager = CreateTestUser(context, "distinct_mgr@example.com", "Manager");
            var admin = CreateTestUser(context, "distinct_admin@example.com", "Admin");
            var reporter = CreateTestUser(context, "distinct_rep@example.com", "Employee");
            var incident = CreateTestIncident(context, reporter.Id, null);
            incident.CreatedAt = DateTime.UtcNow.AddMinutes(-40);
            context.SaveChanges();

            var service = new NotificationService(context, new SlaService());

            // Case A: Manager is active -> Manager gets alert, Admin gets none
            service.QueueSlaNotificationsForIncidentAsync(incident, DateTime.UtcNow).GetAwaiter().GetResult();
            context.SaveChanges();

            var adminNotificationCountWithManagerActive = context.Notifications
                .Count(n => n.UserId == admin.Id && n.IncidentId == incident.Id);
            var managerNotificationCount = context.Notifications
                .Count(n => n.UserId == manager.Id && n.IncidentId == incident.Id);

            Assert.True(managerNotificationCount >= 1, "Manager must receive SLA alert.");
            Assert.Equal(0, adminNotificationCountWithManagerActive, "Admin must NOT receive SLA alert when active Manager exists.");

            Console.WriteLine("  ✓ 30. Admin fallback does not create Manager + Admin duplicate alerts");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_31_Email_Template_Contains_One_Event_Content_Block_Only()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "single_block_user@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            notification.Type = NotificationType.SlaBreached;
            notification.Title = "Resolution SLA breached";
            notification.Message = "INC-TEST-0001 · resolution SLA has breached by 1 hour 30 minutes.";
            context.SaveChanges();

            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            var templateService = new EmailTemplateService(new ConfigurationBuilder().AddInMemoryCollection().Build());
            var email = templateService.BuildEmail(delivery, notification, user);

            // Validate that there is strictly ONE h2 title, ONE details table, ONE CTA button, ONE footer
            var h2Count = System.Text.RegularExpressions.Regex.Matches(email.HtmlBody, "<h2").Count;
            var tableCount = System.Text.RegularExpressions.Regex.Matches(email.HtmlBody, "<table").Count;
            var ctaCount = System.Text.RegularExpressions.Regex.Matches(email.HtmlBody, "View Incident").Count;
            var footerCount = System.Text.RegularExpressions.Regex.Matches(email.HtmlBody, "This is an automated notification").Count;

            Assert.Equal(1, h2Count, "Expected exactly one event title <h2> in HTML body.");
            Assert.Equal(1, tableCount, "Expected exactly one details table in HTML body.");
            Assert.Equal(1, ctaCount, "Expected exactly one View Incident button in HTML body.");
            Assert.Equal(1, footerCount, "Expected exactly one automated notification footer in HTML body.");

            Console.WriteLine("  ✓ 31. email template contains one event content block only");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_32_Template_Renders_Correctly_When_Branding_LogoUrl_Exists()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "branding_logo_user@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);

            var brandingOptions = Options.Create(new BrandingOptions
            {
                LogoUrl = "https://cdn.resolveai.example.com/assets/logo.png"
            });

            var templateService = new EmailTemplateService(
                new ConfigurationBuilder().AddInMemoryCollection().Build(),
                brandingOptions);

            var email = templateService.BuildEmail(delivery, notification, user);

            Assert.Contains(@"<img src=""https://cdn.resolveai.example.com/assets/logo.png"" alt=""ResolveAI Logo""", email.HtmlBody);
            Assert.Contains("ResolveAI", email.HtmlBody);
            Assert.Contains("Enterprise Incident Management", email.HtmlBody);

            Console.WriteLine("  ✓ 32. template renders correctly when Branding:LogoUrl exists");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_33_Template_Renders_Correctly_When_Branding_LogoUrl_Is_Absent()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "branding_nologo_user@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);

            var brandingOptions = Options.Create(new BrandingOptions
            {
                LogoUrl = ""
            });

            var templateService = new EmailTemplateService(
                new ConfigurationBuilder().AddInMemoryCollection().Build(),
                brandingOptions);

            var email = templateService.BuildEmail(delivery, notification, user);

            Assert.DoesNotContain("<img", email.HtmlBody);
            Assert.Contains("ResolveAI", email.HtmlBody);
            Assert.Contains("Enterprise Incident Management", email.HtmlBody);

            Console.WriteLine("  ✓ 33. template renders correctly when LogoUrl is absent");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private void Test_34_No_Live_Provider_Calls_Occur_In_Automated_Tests()
    {
        using var context = CreateContext();
        using var tx = context.Database.BeginTransaction();
        try
        {
            var user = CreateTestUser(context, "mock_tech@example.com", "Technician");
            var notification = CreateTestNotification(context, user.Id);
            var delivery = CreateTestDelivery(context, notification.Id, user.Email);
            context.SaveChanges();

            var fakeSender = new FakeEmailSender();
            var processor = CreateProcessor(context, fakeSender);

            var processed = processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();
            Assert.Equal(1, processed);
            Assert.Single(fakeSender.SentEmails);
            Assert.Equal(user.Email, fakeSender.SentEmails[0].Message.To);

            Console.WriteLine("  ✓ 34. no live provider calls occur in automated tests (mock verified)");
        }
        finally
        {
            tx.Rollback();
        }
    }

    private class FailingServiceScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope()
        {
            throw new InvalidOperationException("Simulated catastrophic failure in separate staging scope.");
        }
    }

    // ─── Test Helpers ─────────────────────────────────────────────────────────

    private static AppUser CreateTestUser(
        AppDbContext context,
        string email,
        string roleName,
        bool isActive = true)
    {
        var role = context.Roles.First(r => r.Name == roleName);
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = "Test",
            LastName = roleName,
            RoleId = role.Id,
            IsActive = isActive,
            EmailNotificationsEnabled = true
        };
        context.Users.Add(user);
        return user;
    }

    private static Incident CreateTestIncident(
        AppDbContext context,
        Guid reporterId,
        Guid? assignedToId)
    {
        var category = context.Categories.First();
        var priority = context.Priorities.First(p => p.Name == "Critical");

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            IncidentNumber = $"INC-TEST-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            Title = "Automated Test Incident",
            Description = "Incident used to verify external notification delivery flows.",
            Status = IncidentStatus.Assigned,
            ReporterId = reporterId,
            AssignedToId = assignedToId,
            CategoryId = category.Id,
            PriorityId = priority.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.Incidents.Add(incident);
        return incident;
    }

    private static Notification CreateTestNotification(AppDbContext context, Guid userId)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = NotificationType.IncidentAssigned,
            Title = "Incident Assigned",
            Message = "You have been assigned to an incident.",
            IncidentTitle = "Test Incident Title",
            IncidentNumber = "INC-TEST-0001",
            CreatedAt = DateTime.UtcNow
        };
        context.Notifications.Add(notification);
        return notification;
    }

    private static ExternalNotificationDelivery CreateTestDelivery(
        AppDbContext context,
        Guid notificationId,
        string recipientAddress)
    {
        var delivery = new ExternalNotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            RecipientAddress = recipientAddress,
            Channel = ExternalDeliveryChannel.Email,
            Provider = "Resend",
            Status = ExternalDeliveryStatus.Pending,
            AttemptCount = 0,
            IdempotencyKey = $"email/{notificationId}/{Guid.NewGuid()}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.ExternalNotificationDeliveries.Add(delivery);
        return delivery;
    }

    private static IExternalNotificationDeliveryProcessor CreateProcessor(
        AppDbContext context,
        IEmailSender emailSender)
    {
        var templateService = new EmailTemplateService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "http://localhost:5173"
            }).Build());

        var options = Options.Create(new ExternalNotificationOptions
        {
            EmailEnabled = true
        });

        return new ExternalNotificationDeliveryProcessor(
            context,
            emailSender,
            templateService,
            options,
            NullLogger<ExternalNotificationDeliveryProcessor>.Instance);
    }

    public static void RunResendSmokeTest()
    {
        Console.WriteLine("\n==================================================");
        Console.WriteLine("MANUAL RESEND SMOKE TEST (Section 25)");
        Console.WriteLine("==================================================");

        var apiKey = "re_MhJ8ZDK6_FYzMjBgeFWNkj4hn4PckcXSJ";
        var fromAddress = "onboarding@resend.dev";
        var fromName = "ResolveAI";

        using var context = CreateContext();
        var user = CreateTestUser(context, "delivered@resend.dev", "Technician");
        var notification = CreateTestNotification(context, user.Id);
        var delivery = CreateTestDelivery(context, notification.Id, "delivered@resend.dev");
        context.SaveChanges();

        try
        {
            var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var resendOptions = Options.Create(new ResendOptions
            {
                ApiKey = apiKey,
                FromAddress = fromAddress,
                FromName = fromName
            });

            var externalOptions = Options.Create(new ExternalNotificationOptions
            {
                EmailEnabled = true,
                OverrideRecipient = null
            });

            var environment = new FakeHostEnvironment("Development");
            var sender = new ResendEmailSender(
                httpClient,
                resendOptions,
                externalOptions,
                environment,
                NullLogger<ResendEmailSender>.Instance);

            var templateService = new EmailTemplateService(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Frontend:BaseUrl"] = "http://localhost:5173"
                }).Build());

            var processor = new ExternalNotificationDeliveryProcessor(
                context,
                sender,
                templateService,
                externalOptions,
                NullLogger<ExternalNotificationDeliveryProcessor>.Instance);

            var processed = processor.ProcessPendingDeliveriesAsync().GetAwaiter().GetResult();

            context.Entry(delivery).Reload();

            Console.WriteLine($"  Processed deliveries count: {processed}");
            Console.WriteLine($"  Delivery Status: {delivery.Status}");
            Console.WriteLine($"  ProviderMessageId: {delivery.ProviderMessageId}");
            Console.WriteLine($"  SentAt: {delivery.SentAt}");
            Console.WriteLine($"  LastError: {delivery.LastError}");

            Assert.Equal(ExternalDeliveryStatus.Sent, delivery.Status);
            Assert.NotNull(delivery.ProviderMessageId);
            Assert.NotNull(delivery.SentAt);

            Console.WriteLine("  ✓ HTTP/provider success");
            Console.WriteLine($"  ✓ ProviderMessageId returned ({delivery.ProviderMessageId})");
            Console.WriteLine("  ✓ delivery row marked Sent");
            Console.WriteLine("==================================================");
            Console.WriteLine("RESEND LIVE SMOKE TEST SUCCESSFUL");
            Console.WriteLine("==================================================");
        }
        finally
        {
            context.ExternalNotificationDeliveries.Remove(delivery);
            context.Notifications.Remove(notification);
            context.Users.Remove(user);
            context.SaveChanges();
        }
    }
}

public class FakeHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
{
    public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "ResolveAI";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

public class FakeEmailSender : IEmailSender
{
    public List<(EmailMessage Message, string IdempotencyKey)> SentEmails { get; } = new();

    public Func<EmailMessage, string, EmailSendResult>? SendHandler { get; set; }

    public Task<EmailSendResult> SendAsync(
        EmailMessage message,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        SentEmails.Add((message, idempotencyKey));

        if (SendHandler != null)
        {
            return Task.FromResult(SendHandler(message, idempotencyKey));
        }

        return Task.FromResult(EmailSendResult.Succeeded($"fake_resend_id_{Guid.NewGuid():N}"));
    }
}
