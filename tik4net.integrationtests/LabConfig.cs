// LabConfig.cs — every App.config setting the suite reads, through the router profile the run selected.
//
// The lab has two routers (README.md, "The lab routers"). A run picks one with the TIK4NET_ROUTER environment
// variable (run-integration-tests.ps1 -Router <name>); nothing in a tracked file changes. A profile is a set of
// App.config keys prefixed with its name — "chr2.host" — and each overrides the unprefixed key of the same name;
// a key the profile does not list keeps the default. With no variable set, the unprefixed keys are the whole story.
//
// An environment variable rather than a runsettings parameter: several tests read their router in static class
// set-up, where no TestContext exists, and a child test host inherits the variable like TIK4NET_WIRETRACE.

using System;
using System.Configuration;

namespace tik4net.integrationtests
{
    /// <summary>The suite's App.config settings, as the selected router profile overrides them.</summary>
    public static class LabConfig
    {
        /// <summary>The environment variable naming the router profile; unset or empty = the default router.</summary>
        public const string ProfileVariable = "TIK4NET_ROUTER";

        /// <summary>The selected profile, or <c>null</c> for the default router.</summary>
        public static string Profile
        {
            get
            {
                string name = Environment.GetEnvironmentVariable(ProfileVariable)?.Trim();
                if (string.IsNullOrEmpty(name))
                    return null;

                // A profile the file does not define must not quietly run against the default router: that is
                // how a typo ("chr3") would measure the wrong machine and report it as the one asked for.
                if (ConfigurationManager.AppSettings[name + ".host"] == null)
                    throw new InvalidOperationException(
                        $"{ProfileVariable}={name}, but App.config defines no '{name}.host'. A router profile is a set of "
                        + $"'{name}.<key>' entries; add them, or unset {ProfileVariable} for the default router.");
                return name;
            }
        }

        /// <summary>
        /// The value of <paramref name="key"/> for the selected router: the profile's own entry when it has one (an
        /// empty value included — that is how a profile switches a setting off), otherwise the unprefixed entry.
        /// </summary>
        public static string Get(string key) => GetFor(Profile, key);

        /// <summary>
        /// The value of <paramref name="key"/> for the named <paramref name="profile"/> (<c>null</c> = the default router),
        /// whatever <see cref="ProfileVariable"/> selects — for a test that talks to two lab routers at once.
        /// </summary>
        public static string GetFor(string profile, string key)
        {
            if (profile != null)
            {
                string own = ConfigurationManager.AppSettings[profile + "." + key];
                if (own != null)
                    return own;
            }
            return ConfigurationManager.AppSettings[key];
        }
    }
}
