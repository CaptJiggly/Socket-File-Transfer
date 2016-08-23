using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace file_transfer
{
    public enum QueueType : byte
    {
        Download,
        Upload
    }

    public class TransferQueue
    {
        public static TransferQueue CreateUploadQueue(TransferClient client, string fileName)
        {
            try
            {
                //We will create a new upload queue
                var queue = new TransferQueue();
                //Set our filename
                queue.Filename = Path.GetFileName(fileName);
                //Set our client
                queue.Client = client;
                //Set our queue type to upload.
                queue.Type = QueueType.Upload;
                //Create our file stream for reading.
                queue.FS = new FileStream(fileName, FileMode.Open);
                //Create our transfer thread
                queue.Thread = new Thread(new ParameterizedThreadStart(transferProc));
                queue.Thread.IsBackground = true;
                //Generate our ID
                queue.ID = Program.Rand.Next();
                //Set our length to the size of the file.
                queue.Length = queue.FS.Length;
                return queue;
            }
            catch
            {
                //If something goes wrong, return null
                return null;
            }
        }

        public static TransferQueue CreateDownloadQueue(TransferClient client, int id, string saveName, long length)
        {
            try
            {
                //Same as above with some changes.
                var queue = new TransferQueue();
                queue.Filename = Path.GetFileName(saveName);
                queue.Client = client;
                queue.Type = QueueType.Download;
                //Create our file stream for writing.
                queue.FS = new FileStream(saveName, FileMode.Create);
                //Fill the stream will 0 bytes based on the real size. So we can index write.
                queue.FS.SetLength(length);
                queue.Length = length;
                //Instead of generating an ID, we will set the ID that has been sent.
                queue.ID = id;
                return queue;
            }
            catch
            {
                return null;
            }
        }

        //This will be the size of our read buffer.
        private const int FILE_BUFFER_SIZE = 8175;
        //This will be the single read buffer every transfer queue will use to save memory.
        private static byte[] file_buffer = new byte[FILE_BUFFER_SIZE];
        //This will be used for pausing uploads.
        private ManualResetEvent pauseEvent;
        //This will be the generated ID for each transfer.
        public int ID;
        //This will hold the progress and last progress (For checks) for the queues.
        public int Progress, LastProgress;
        //These will hold our transferred bytes, current read/write index and the size of the file.
        public long Transferred;
        public long Index;
        public long Length;
        
        public bool Running;
        public bool Paused;

        //This holds the filename for reading/writing.
        public string Filename;

        public QueueType Type;
        //This will hold our transfer client
        public TransferClient Client;
        //This will hold our upload thread.
        public Thread Thread;
        //This will hold our file stream for reading/writing.
        public FileStream FS;

        private TransferQueue()
        {
            //When the instance is create, create a new ManualResetEvent.
            pauseEvent = new ManualResetEvent(true);
            Running = true;
        }

        public void Start()
        {
            //We will start our upload thread with the current instance as the parameter.
            Running = true;
            Thread.Start(this);
        }

        public void Stop()
        {
            Running = false;
        }

        public void Pause()
        {
            //If it is not paused, reset the event so the upload thread will block.
            if (!Paused)
            {
                pauseEvent.Reset();
            }
            else //If it is already paused, set the event so the thread can continue.
            {
                pauseEvent.Set();
            }

            Paused = !Paused; //Flip the paused variable.
        }

        public void Close()
        {
            try
            {
                //Remove the current queue from the client transfer list.
                Client.Transfers.Remove(ID);
            }
            catch { }
            Running = false;
            //Close the stream
            FS.Close();
            //Dispose the ResetEvent.
            pauseEvent.Dispose();

            Client = null;
        }

        public void Write(byte[] bytes, long index)
        {
            //Lock the current instance, so only one write at a time is permitted.
            lock (this)
            {
                //Set the stream position to our current write index we receive.
                FS.Position = index;
                //Write the bytes to the stream.
                FS.Write(bytes, 0, bytes.Length);
                //Increase the amount of data we received
                Transferred += bytes.Length;
            }
        }

        private static void transferProc(object o)
        {
            //Cast our transfer queue from the parameter.
            TransferQueue queue = (TransferQueue)o;

            //If Running is true, the thread will keep going
            //If queue.Index is not the file length, the thread will continue.
            while (queue.Running && queue.Index < queue.Length)
            {
                //We will call WaitOne to see if we're paused or not.
                //If we are, it will block until notified.
                queue.pauseEvent.WaitOne();

                //Just in case the transfer was paused then stopped, check to see if we're still running
                if (!queue.Running)
                {
                    break;
                }

                //Lock the file buffer so only one queue can use it at a time.
                lock (file_buffer)
                {
                    //Set the read position to our current position
                    queue.FS.Position = queue.Index;

                    //Read a chunk into our buffer.
                    int read = queue.FS.Read(file_buffer, 0, file_buffer.Length);

                    //Create our packet writer and send our chunk packet.
                    PacketWriter pw = new PacketWriter();

                    pw.Write((byte)Headers.Chunk);
                    pw.Write(queue.ID);
                    pw.Write(queue.Index);
                    pw.Write(read);
                    pw.Write(file_buffer, 0, read);

                    /*The reason the buffer size is 8175 is so it'll be about 8 kilobytes
                     * It should be 8192, but its 8191. I missed a byte since I had to make a quick change, but eh.
                     * 4 Bytes = ID
                     * 8 Bytes = Index
                     * 4 Bytes = read
                     * 8175 Bytes = file_buffer
                     * All together (If the file buffer is full) 8192 Bytes
                     * 
                     */

                    //Increase our data transffered and read index.
                    queue.Transferred += read;
                    queue.Index += read;

                    //Send our data
                    queue.Client.Send(pw.GetBytes());

                    //Get our progress
                    queue.Progress = (int)((queue.Transferred * 100) / queue.Length);

                    if (queue.LastProgress < queue.Progress)
                    {
                        queue.LastProgress = queue.Progress;

                        queue.Client.callProgressChanged(queue);
                    }

                    //Sleep for a millisecond so we don't kill our CPU
                    Thread.Sleep(1);
                }
            }
            queue.Close(); //Once the loop is broken, close the queue.
        }
    }
}
