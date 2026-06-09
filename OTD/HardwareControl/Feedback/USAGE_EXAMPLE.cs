// SPDX-License-Identifier: GPL-3.0-or-later
//
// OpenTrainDrive - FeedbackControl Example
// Shows how to use FeedbackService and FeedbackManager

/*
EXAMPLE USAGE:

    using OTD.HardwareControl.Feedback;
    using OTD.HardwareControl.CommandStation.LoDi;

    // Start feedback service from config
    using var feedbackService = new FeedbackService();
    await feedbackService.InitializeAsync();

    // Subscribe to state changes
    feedbackService.ContactStateChanged += (s, e) =>
    {
        Console.WriteLine($"Contact {e.State.Key}: {(e.State.IsOccupied ? "OCCUPIED" : "FREE")}");
    };

    feedbackService.ModuleStateReceived += (s, e) =>
    {
        Console.WriteLine($"Module {e.ModuleAddress} received: {string.Join(", ", 
            e.Contacts.Select(c => c.IsOccupied ? "X" : "."))}");
    };

    // Query current state
    var feedback = feedbackService.Manager.GetContactState(1, 1);
    if (feedback != null)
        Console.WriteLine($"Section 1/1: {(feedback.IsOccupied ? "Occupied" : "Free")}");

    // List all known contacts
    foreach (var contact in feedbackService.Manager.GetAllStates())
        Console.WriteLine($"{contact.Key}: {contact.IsOccupied}");

    // Clean shutdown
    await feedbackService.ShutdownAsync();

CONFIGURATION (AppData/feedback.xml):

    <feedbacks>
      <provider uid="lodi-s88-main" driver="lodi-s88-commander">
        <connection ip="192.168.1.101" port="11092" />
        <startup queryOnStartup="true" subscribeOnStartup="true" />
        <modules>
          <module address="1" />
          <module address="2" />
        </modules>
      </provider>
    </feedbacks>

ADDITIONAL: Manually initialize one provider

    var provider = FeedbackConfigLoader.LoadProviderByUid("lodi-s88-main");
    using var commander = new LoDiS88Commander();
    var manager = new FeedbackManager();
    
    manager.SubscribeTo(commander);
    await FeedbackInitializer.InitializeLoDiS88Async(commander, provider);
    
    // Use manager...
    var state = manager.GetContactState(1, 1);
*/

