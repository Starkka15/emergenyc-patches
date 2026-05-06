using System.Runtime.CompilerServices;

// Allow CommunityFixes to call internal SDK methods (Raise* bridges, V12)
[assembly: InternalsVisibleTo("EmergeNYC.CommunityFixes")]
