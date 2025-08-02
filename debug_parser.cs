using Frimerki.Protocols.Imap;

var parser = new ImapCommandParser();

// Test LOGIN command
var loginCmd = parser.ParseCommand("a2 LOGIN \"test@example.com\" \"password\"");
Console.WriteLine($"LOGIN - Arguments count: {loginCmd.Arguments.Count}");
for (int i = 0; i < loginCmd.Arguments.Count; i++)
    Console.WriteLine($"  Arg {i}: '{loginCmd.Arguments[i]}'");

// Test LIST command
var listCmd = parser.ParseCommand("a3 LIST \"\" \"*\"");
Console.WriteLine($"\nLIST - Arguments count: {listCmd.Arguments.Count}");
for (int i = 0; i < listCmd.Arguments.Count; i++)
    Console.WriteLine($"  Arg {i}: '{listCmd.Arguments[i]}'");

// Test FETCH command
var fetchCmd = parser.ParseCommand("a5 FETCH 1:5 (FLAGS BODY[HEADER])");
Console.WriteLine($"\nFETCH - Arguments count: {fetchCmd.Arguments.Count}");
for (int i = 0; i < fetchCmd.Arguments.Count; i++)
    Console.WriteLine($"  Arg {i}: '{fetchCmd.Arguments[i]}'");

// Test SELECT command
var selectCmd = parser.ParseCommand("a4 SELECT \"INBOX\"");
Console.WriteLine($"\nSELECT - Arguments count: {selectCmd.Arguments.Count}");
for (int i = 0; i < selectCmd.Arguments.Count; i++)
    Console.WriteLine($"  Arg {i}: '{selectCmd.Arguments[i]}'");
