using System.Runtime.CompilerServices;

// The release pipeline encrypts the content pack with the same compiled key the player decrypts with.
[assembly: InternalsVisibleTo("UOClient.Editor")]
