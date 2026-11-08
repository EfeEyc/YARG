using System;
using System.IO;
using System.Runtime.InteropServices;
using AOT;
using ManagedBass;

namespace YARG.Audio.BASS
{
    public static class BassStreamProcedures
    {
        private sealed class StreamContext
        {
            public readonly Stream Stream;
            public readonly long Start;
            public readonly long Length;

            public StreamContext(Stream stream)
            {
                Stream = stream;
                Start = stream.Position;
                Length = stream.Length - Start;
            }
        }

        public static readonly FileProcedures Procedures = new()
        {
            Close = OnClose,
            Length = OnLength,
            Read = OnRead,
            Seek = OnSeek,
        };

        public static IntPtr CreateUserContext(Stream stream)
        {
            var context = new StreamContext(stream);
            var handle = GCHandle.Alloc(context, GCHandleType.Normal);
            return GCHandle.ToIntPtr(handle);
        }

        public static void FreeUserContext(IntPtr user)
        {
            if (user != IntPtr.Zero)
            {
                var handle = GCHandle.FromIntPtr(user);
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        [MonoPInvokeCallback(typeof(FileCloseProcedure))]
        private static void OnClose(IntPtr user)
        {
            if (user == IntPtr.Zero)
            {
                return;
            }

            var handle = GCHandle.FromIntPtr(user);
            if (!handle.IsAllocated)
            {
                return;
            }

            try
            {
                if (handle.Target is StreamContext context)
                {
                    context.Stream.Close();
                }
            }
            catch
            {
                // Ignore stream close errors
            }
            finally
            {
                handle.Free();
            }
        }

        [MonoPInvokeCallback(typeof(FileLengthProcedure))]
        private static long OnLength(IntPtr user)
        {
            if (user == IntPtr.Zero)
            {
                return 0;
            }

            var handle = GCHandle.FromIntPtr(user);
            if (handle.IsAllocated && handle.Target is StreamContext context)
            {
                return context.Length;
            }

            return 0;
        }

        [MonoPInvokeCallback(typeof(FileReadProcedure))]
        private static int OnRead(IntPtr buffer, int length, IntPtr user)
        {
            if (user == IntPtr.Zero)
            {
                return 0;
            }

            try
            {
                var handle = GCHandle.FromIntPtr(user);
                if (handle.IsAllocated && handle.Target is StreamContext context)
                {
                    unsafe
                    {
                        return context.Stream.Read(new Span<byte>((byte*) buffer, length));
                    }
                }
            }
            catch
            {
                return 0;
            }

            return 0;
        }

        [MonoPInvokeCallback(typeof(FileSeekProcedure))]
        private static bool OnSeek(long offset, IntPtr user)
        {
            if (user == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var handle = GCHandle.FromIntPtr(user);
                if (handle.IsAllocated && handle.Target is StreamContext context)
                {
                    context.Stream.Seek(offset + context.Start, SeekOrigin.Begin);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
