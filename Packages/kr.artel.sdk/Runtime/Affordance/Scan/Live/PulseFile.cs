using System.IO;
using System.Text;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// Writes each changed reading as one line, so a reading can be watched with no socket at all.
    /// </summary>
    /// <remarks>
    /// A file rather than a connection because the package must stay useful before anything is
    /// listening. A tester tailing this sees the same documents an agent would, in the same order,
    /// and a channel that can be read with <c>tail -f</c> is one whose faults are visible.
    ///
    /// One document per line and never reformatted, so the file is what arrived rather than an
    /// account of it. Appended rather than replaced: the state before an input is what makes the
    /// state after it mean anything, and a file holding only the latest reading throws away the half
    /// of every pair that says what changed.
    ///
    /// The handle is held open. Opening and closing per reading would cost more than composing one,
    /// and the flush after each line is what makes a reader see it without waiting for the buffer.
    /// </remarks>
    public sealed class PulseFile : IPulseSink, System.IDisposable
    {
        private const string FileName = "artel-pulse.jsonl";

        /// <summary>Where the readings are written.</summary>
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

        private StreamWriter _writer;

        /// <summary>Opens the file, or answers null having said why it could not.</summary>
        /// <remarks>
        /// Null rather than an object that fails on every write. A sink that cannot be opened is a
        /// thing to find out about before the game starts rather than once a beat afterwards.
        ///
        /// Opened so that others may read it while it is being written. A file of one document per
        /// line exists to be followed as it grows — that is the whole reason this is the default
        /// sink — and a handle that locks readers out defeats the thing it was made for. Measured:
        /// a reader polling once a second took the file for the moment it read, and the next attempt
        /// to start watching failed with a sharing violation and no channel at all.
        ///
        /// <see cref="StreamWriter"/>'s own constructors give no way to say this, so the stream is
        /// made first and handed over.
        /// </remarks>
        public static PulseFile Open(bool append = true)
        {
            try
            {
                var stream = new FileStream(
                    Path,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.ReadWrite);

                return new PulseFile
                {
                    _writer = new StreamWriter(stream, new UTF8Encoding(false))
                };
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[Artel] Could not open " + Path + ": " + exception.Message);
                return null;
            }
        }

        public void Send(string document)
        {
            if (_writer == null)
            {
                return;
            }

            _writer.Write(document);
            _writer.Write('\n');
            _writer.Flush();
        }

        public void Dispose()
        {
            if (_writer == null)
            {
                return;
            }

            _writer.Dispose();
            _writer = null;
        }
    }
}
