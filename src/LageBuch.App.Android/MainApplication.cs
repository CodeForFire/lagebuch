using Android.Runtime;

namespace LageBuch.App.Android;

/// <summary>Android application entry point — required boilerplate for every .NET Android app.</summary>
[Application]
public class MainApplication : Application
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
    {
    }
}
