namespace LageBuch.AppLogic.ViewModels;

/// <summary>What <see cref="RoleAssignmentDialogViewModel"/> does to the running Funktion it was opened on.</summary>
public enum RoleAssignmentDialogMode
{
    /// <summary>Corrects the holder's details; the assignment stays, Von with it.</summary>
    Edit,

    /// <summary>Ends the assignment and starts one for the person named: a handover.</summary>
    Transfer,
}
