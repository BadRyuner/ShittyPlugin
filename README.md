This is a plugin to [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL).

Just compile latest Cpp2Il, compile this shitty plugin and copy "Cpp2IL.Plugin.BetterAssemblyOutput.dll" to "yourCompiledCpp2ILBinaries/Plugins/"

And run `.\Cpp2IL.exe --output-as shitty_il --game-path "path to your silly game"`

Get some shitty il with ILSpy (because plugin shitty it creates a bit of stack underflow because of which crappy C# can only display ILSpy). 
But ILSpy gives a bunch of shit in the form of long Unsafe expressions! 
Unluck bro( dnSpy can display some simple methods in normal way, but you can find what the fuck in this silly plugin code generates fucking stack underflow, fix it and get normal output which can be opened in dnspy.

I don't know why I made it. Ignore it.

The best my shitty plug-in can produce:
![image](https://github.com/user-attachments/assets/fc441ea3-e86d-4829-9e55-5bce213f3fa8)
