using System;
using System.Runtime.InteropServices;

namespace CvAut
{
    internal static class NativeTemplateCodec
    {
        private const string LibraryName = "simplimixi_native";

        public static bool TryDecode(ReadOnlySpan<byte> encryptedBytes, byte[] output, out int outputLength)
        {
            outputLength = 0;
            if (encryptedBytes.IsEmpty || output.Length == 0)
            {
                return false;
            }

            byte[] input = encryptedBytes.ToArray();
            try
            {
                int result = DecodeTemplate(input, input.Length, output, output.Length, out outputLength);
                return result == 0 && outputLength >= 0 && outputLength <= output.Length;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }

        [DllImport(LibraryName, EntryPoint = "simplimixi_decode_template", CallingConvention = CallingConvention.Cdecl)]
        private static extern int DecodeTemplate(
            byte[] input,
            int inputLength,
            byte[] output,
            int outputCapacity,
            out int outputLength);
    }
}
