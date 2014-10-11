static void Main(string[] args)
{
  Console.WriteLine(
        AmazingPrintf("Chris is %d years old, from %s!", 
            1409, "Estonia"));
}

[DllImport("msvcrt.dll", CallingConvention=CallingConvention.Cdecl)]
private unsafe static extern int vsprintf(
    StringBuilder buffer, 
    string format, 
    char  *ptr);

[DllImport("msvcrt.dll", CallingConvention=CallingConvention.Cdecl)]
private unsafe static extern int _vscprintf(
    string format, 
    char  *ptr);


unsafe static string BuildStackAndCall(
    string format, 
    object[] parameters, 

    char ** temp,

    int index)
{
    if(index == parameters.Length){

        char *args = (char*)temp;

        var sb = new StringBuilder();
        sb.Capacity = _vscprintf(format, args) + 1;

        // what to do with return value?!
        vsprintf(sb, format, args);

        return sb.ToString();
    }

    if(parameters[index] is int){
        temp[index] = (char *)(int)parameters[index];
        return BuildStackAndCall(format, parameters, temp, ++index);
    }

    // vsprintf likes ASCII only!
    // let's do simple conversion?!
    if(parameters[index] is string){
        byte[] asciiBytes = Encoding.ASCII
                                .GetBytes((string)parameters[index])
                                .Concat(new byte[]{(byte)'\0'}).ToArray();

        fixed(byte* bytePtr = asciiBytes){
            temp[index] =  (char*)bytePtr;
            return BuildStackAndCall(format, parameters, temp, ++index);
        }
    }

    throw new NotSupportedException("BOOM!");
}

unsafe static string AmazingPrintf(
    string format, 
    params object[] parameters)
{
    if(!parameters.Any()) 
        return format;

    // todo: validation of strFormat && parameters?!
    // otherwise it's possible to blow up stack.

    fixed(char** temp = new char*[parameters.Length])
    {
        char **t = temp;
        return BuildStackAndCall(
                    format, 
                    parameters, 
                    (char**)&t, 0);
    }
}