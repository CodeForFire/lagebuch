using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class OperatorPromptViewModelTests
{
    [Fact]
    public void Confirm_names_the_missing_operator_instead_of_going_grey()
    {
        var vm = new OperatorPromptViewModel();

        Assert.True(vm.ConfirmCommand.CanExecute(null)); // the press is the question (#412)
        Assert.Null(vm.OperatorNameError); // quiet until asked
        vm.ConfirmCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.OperatorNameError);
        Assert.Null(vm.Result); // nobody is documenting yet, so the prompt stays up

        vm.OperatorName = "Müller";
        Assert.Null(vm.OperatorNameError);
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(vm.Result);
    }

    private static readonly Person[] Roster =
    {
        new("Schmidt", "Anna", null, "FFB 12/2", null),
        new("Huber", "Max", null, null, null),
    };

    [Fact]
    public void Offers_own_personnel_as_name_suggestions()
    {
        var vm = new OperatorPromptViewModel(personnel: Roster);

        Assert.Equal(new[] { "Schmidt, Anna", "Huber, Max" }, vm.PersonOptions);
    }

    [Fact]
    public void Offers_only_own_personnel_as_name_suggestions()
    {
        var roster = Roster.Append(new Person("Nachbar", "Nora", null, "Florian Nachbarort 1", null, IsOwn: false)).ToArray();

        var vm = new OperatorPromptViewModel(personnel: roster);

        Assert.Equal(new[] { "Schmidt, Anna", "Huber, Max" }, vm.PersonOptions);
    }

    [Fact]
    public void A_foreign_person_typed_by_hand_still_fills_the_call_sign()
    {
        var roster = Roster.Append(new Person("Nachbar", "Nora", null, "Florian Nachbarort 1", null, IsOwn: false)).ToArray();
        var vm = new OperatorPromptViewModel(personnel: roster);

        vm.OperatorName = "Nachbar, Nora";

        Assert.Equal("Florian Nachbarort 1", vm.OperatorCallSign);
    }

    [Fact]
    public void Picking_a_person_fills_a_blank_call_sign()
    {
        var vm = new OperatorPromptViewModel(personnel: Roster);

        vm.OperatorName = "Schmidt, Anna";

        Assert.Equal("FFB 12/2", vm.OperatorCallSign);
    }

    [Fact]
    public void Picking_a_person_keeps_a_call_sign_typed_by_hand()
    {
        var vm = new OperatorPromptViewModel(personnel: Roster) { OperatorCallSign = "ELW 1" };

        vm.OperatorName = "Schmidt, Anna";

        Assert.Equal("ELW 1", vm.OperatorCallSign);
    }

    [Fact]
    public void A_name_outside_the_roster_stays_allowed()
    {
        var vm = new OperatorPromptViewModel(personnel: Roster) { OperatorName = "Gast" };

        vm.ConfirmCommand.Execute(null);

        Assert.Equal("Gast", vm.Result!.Display);
    }

    [Fact]
    public void A_handover_prompt_names_the_operator_being_replaced()
    {
        var vm = new OperatorPromptViewModel(previous: new SessionOperator("Müller", "FFB 12/1"));

        Assert.True(vm.IsHandover);
        Assert.Equal("Lagebuchführer wechseln", vm.Title);
        Assert.Equal("Müller (FFB 12/1)", vm.PreviousOperatorDisplay);
    }

    [Fact]
    public void Confirm_builds_session_operator_with_callsign()
    {
        var vm = new OperatorPromptViewModel { OperatorName = "Müller", OperatorCallSign = "FFB 12/1" };
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(vm.Result);
        Assert.Equal("Müller (FFB 12/1)", vm.Result!.Display);
    }

    [Fact]
    public void Confirm_raises_property_changed_for_Result()
    {
        var vm = new OperatorPromptViewModel { OperatorName = "Müller" };
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperatorPromptViewModel.Result))
            {
                raised = true;
            }
        };
        vm.ConfirmCommand.Execute(null);
        Assert.True(raised);
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void CallSignOptions_default_to_empty()
    {
        var vm = new OperatorPromptViewModel();
        Assert.Empty(vm.CallSignOptions);
    }

    [Fact]
    public void CallSignOptions_expose_the_supplied_list()
    {
        var options = new[] { "FFB 1/40/1", "Aich 42/1" };
        var vm = new OperatorPromptViewModel(callSignOptions: options);
        Assert.Equal(options, vm.CallSignOptions);
    }

    [Fact]
    public void Call_sign_stays_free_text_even_when_not_in_the_options()
    {
        // The dropdown is a hint, not a closed set: an off-list Funkrufname must still confirm.
        var vm = new OperatorPromptViewModel(callSignOptions: new[] { "FFB 1/40/1" })
        {
            OperatorName = "Müller",
            OperatorCallSign = "Land 9",
        };
        vm.ConfirmCommand.Execute(null);
        Assert.Equal("Müller (Land 9)", vm.Result!.Display);
    }

    [Fact]
    public void Join_flow_names_the_missing_pin()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { Host = "elw-1" };
        var connectRequested = false;
        vm.ConnectRequested += (_, _) => connectRequested = true;

        vm.ConfirmCommand.Execute(null);
        Assert.Equal(ValidationMessages.Required, vm.PinError); // host given, PIN still missing
        Assert.Null(vm.HostError); // only the offending field is named
        Assert.False(connectRequested);

        vm.Pin = "1234";
        Assert.Null(vm.PinError);
        vm.ConfirmCommand.Execute(null);
        Assert.True(connectRequested);
    }

    // --- The two-stage join (#459) ---
    private static Incident JoinedIncident()
    {
        var incident = Incident.Start(
            new FixedClock(new DateTimeOffset(2026, 9, 28, 14, 0, 0, TimeSpan.FromHours(2))),
            new SessionOperator("Host"),
            keyword: "B3 Wohnung");
        incident.SetAddress("Hauptstraße 5", "Nord");
        return incident;
    }

    private static readonly MasterDataSet HostMasterData = MasterDataSet.Empty with
    {
        Vehicles = new[] { new Vehicle("Host-Wache", "Florian Host 40/1", 9) },
        Personnel = Roster.Append(new Person("Nachbar", "Nora", null, "Florian Nachbarort 1", null, IsOwn: false)).ToArray(),
    };

    [Fact]
    public void Join_prompt_asks_only_for_host_and_pin_first()
    {
        var vm = new OperatorPromptViewModel(collectHost: true);

        Assert.True(vm.IsHostStage);
        Assert.False(vm.AsksForOperator);
        Assert.Equal("Mit Gerät verbinden", vm.Title);
        Assert.Equal("VERBINDEN", vm.ConfirmLabel);
        Assert.Null(vm.JoinedIncidentDisplay);
    }

    [Fact]
    public void Confirming_the_host_step_raises_connect_instead_of_a_result()
    {
        // No name yet, and none demanded: it is not asked for in this stage.
        var vm = new OperatorPromptViewModel(collectHost: true) { Host = "elw-1", Pin = "1234" };
        var connectRequested = false;
        vm.ConnectRequested += (_, _) => connectRequested = true;

        vm.ConfirmCommand.Execute(null);

        Assert.True(connectRequested);
        Assert.Null(vm.Result);
        Assert.Null(vm.OperatorNameError);
    }

    [Fact]
    public void Operator_stage_names_the_incident_and_offers_the_hosts_own_personnel_and_call_signs()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { Host = "elw-1", Pin = "1234" };

        vm.ShowOperatorStage(JoinedIncident(), HostMasterData);

        Assert.True(vm.IsOperatorStage);
        Assert.False(vm.IsHostStage);
        Assert.True(vm.AsksForOperator);
        Assert.Equal("Wer dokumentiert?", vm.Title);
        Assert.Equal("BESTÄTIGEN", vm.ConfirmLabel);
        Assert.Equal("B3 Wohnung · Hauptstraße 5, Nord", vm.JoinedIncidentDisplay);
        Assert.Equal(new[] { "Schmidt, Anna", "Huber, Max" }, vm.PersonOptions);
        Assert.Equal(HostMasterData.RadioCallSigns, vm.CallSignOptions);
        Assert.Null(vm.OperatorNameError); // a fresh stage starts quiet

        vm.OperatorName = "Schmidt, Anna"; // the host's roster drives the Funkrufname prefill too
        Assert.Equal("FFB 12/2", vm.OperatorCallSign);
    }

    [Fact]
    public void An_incident_without_stichwort_or_address_is_still_named()
    {
        var vm = new OperatorPromptViewModel(collectHost: true);

        vm.ShowOperatorStage(
            Incident.Start(new FixedClock(DateTimeOffset.UnixEpoch), new SessionOperator("Host")),
            MasterDataSet.Empty);

        Assert.Equal("Unbenannter Einsatz", vm.JoinedIncidentDisplay);
    }

    [Fact]
    public void Confirming_the_operator_stage_produces_the_result()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { Host = "elw-1", Pin = "1234" };
        vm.ShowOperatorStage(JoinedIncident(), HostMasterData);

        vm.ConfirmCommand.Execute(null);
        Assert.Equal(ValidationMessages.Required, vm.OperatorNameError);
        Assert.Null(vm.Result);

        vm.OperatorName = "Huber, Max";
        vm.ConfirmCommand.Execute(null);
        Assert.Equal("Huber, Max", vm.Result!.Display);
    }

    [Fact]
    public void A_failure_after_the_host_step_returns_to_host_and_pin()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { Host = "elw-1", Pin = "1234" };
        vm.ShowOperatorStage(JoinedIncident(), HostMasterData);
        vm.OperatorName = "Huber, Max";

        vm.ReportJoinFailure("Verbindung zu elw-1 nicht möglich.", certificateChanged: false);

        Assert.True(vm.IsHostStage);
        Assert.Null(vm.JoinedIncidentDisplay);
        Assert.Equal("Verbindung zu elw-1 nicht möglich.", vm.ErrorMessage);
        Assert.Equal(string.Empty, vm.Pin);
        Assert.Equal("Huber, Max", vm.OperatorName); // kept for the retry
    }

    [Fact]
    public void Pin_is_not_required_when_not_joining()
    {
        // The new-incident / continue-editing flows never show the PIN field, so it must not
        // refuse them -- and must not be named as missing either.
        var vm = new OperatorPromptViewModel { OperatorName = "Müller" };

        vm.ConfirmCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.Null(vm.PinError);
        Assert.Null(vm.HostError);
    }

    [Fact]
    public void Busy_prompt_cannot_confirm_even_with_valid_fields()
    {
        // While a join attempt is in flight (#182), the Confirm button must not accept a second click.
        var vm = new OperatorPromptViewModel(collectHost: true) { OperatorName = "Müller", Host = "elw-1", Pin = "1234" };
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.IsBusy = true;
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Reporting_a_join_failure_clears_the_pin_but_keeps_the_rest()
    {
        var vm = new OperatorPromptViewModel(collectHost: true)
        {
            OperatorName = "Müller",
            Host = "elw-1",
            Pin = "9999",
        };

        vm.ReportJoinFailure("Falsche PIN.", certificateChanged: false);

        Assert.Equal("Falsche PIN.", vm.ErrorMessage);
        Assert.False(vm.CertificateChanged);
        Assert.Equal(string.Empty, vm.Pin);
        Assert.Equal("elw-1", vm.Host);
        Assert.Equal("Müller", vm.OperatorName);
    }

    [Fact]
    public void Reporting_a_certificate_changed_failure_sets_the_flag()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { OperatorName = "Müller", Host = "elw-1", Pin = "1234" };

        vm.ReportJoinFailure("Zertifikat geändert.", certificateChanged: true);

        Assert.True(vm.CertificateChanged);
    }

    [Fact]
    public void Reporting_a_join_failure_resets_result_so_confirm_can_fire_again()
    {
        var vm = new OperatorPromptViewModel(collectHost: true) { OperatorName = "Müller", Host = "elw-1", Pin = "1234" };
        vm.ShowOperatorStage(JoinedIncident(), HostMasterData);
        vm.ConfirmCommand.Execute(null);
        Assert.NotNull(vm.Result);

        vm.ReportJoinFailure("Verbindung zu elw-1 nicht möglich.", certificateChanged: false);
        Assert.Null(vm.Result);

        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperatorPromptViewModel.Result))
            {
                raised = true;
            }
        };
        vm.Pin = "1234";
        vm.ShowOperatorStage(JoinedIncident(), HostMasterData);
        vm.ConfirmCommand.Execute(null);

        Assert.True(raised);
        Assert.NotNull(vm.Result);
    }

    [Fact]
    public void Cancel_label_says_close_dialog_when_idle_and_abort_connection_when_busy()
    {
        // #196: the same button drives both effects, so its label must say which one currently
        // applies -- plain "ABBRECHEN" reads as "close this dialog", which would be wrong while busy.
        var vm = new OperatorPromptViewModel(collectHost: true);
        Assert.Equal("ABBRECHEN", vm.CancelLabel);

        vm.IsBusy = true;
        Assert.Equal("VERBINDUNG ABBRECHEN", vm.CancelLabel);

        vm.IsBusy = false;
        Assert.Equal("ABBRECHEN", vm.CancelLabel);
    }

    [Fact]
    public void Cancel_command_raises_Cancelled_when_idle()
    {
        var vm = new OperatorPromptViewModel();
        var cancelled = false;
        var cancelJoinRaised = false;
        vm.Cancelled += (_, _) => cancelled = true;
        vm.CancelJoinRequested += (_, _) => cancelJoinRaised = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelled);
        Assert.False(cancelJoinRaised);
    }

    [Fact]
    public void Cancel_command_raises_CancelJoinRequested_while_busy()
    {
        // #196: a busy dialog has a connection attempt in flight to abort, not just an overlay to
        // dismiss -- the two must not be conflated, or cancelling a join would skip straight past
        // HomeViewModel.JoinDeviceCancelCommand and leave the connect attempt running.
        var vm = new OperatorPromptViewModel(collectHost: true) { IsBusy = true };
        var cancelled = false;
        var cancelJoinRaised = false;
        vm.Cancelled += (_, _) => cancelled = true;
        vm.CancelJoinRequested += (_, _) => cancelJoinRaised = true;

        vm.CancelCommand.Execute(null);

        Assert.True(cancelJoinRaised);
        Assert.False(cancelled);
    }

    [Fact]
    public void Reset_trust_command_raises_the_reset_trust_requested_event()
    {
        var vm = new OperatorPromptViewModel(collectHost: true);
        var raised = false;
        vm.ResetTrustRequested += (_, _) => raised = true;

        vm.ResetTrustCommand.Execute(null);

        Assert.True(raised);
    }
}
