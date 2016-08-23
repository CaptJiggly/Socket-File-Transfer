using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using file_transfer;

public partial class Main : Form
{
    //This will hold our listener. We will only need to create one instance of this.
    private Listener listener;
    //This will hold our transfer client.
    private TransferClient transferClient;
    //This will hold our output folder.
    private string outputFolder;
    //This will hold our overall progress timer.
    private Timer tmrOverallProg;
    //This is our variable to determine of the server is running or not to accept another connection if our client
    //Disconnects
    private bool serverRunning;

    public Main()
    {
        InitializeComponent();
        //Create the listener and register the event.
        listener = new Listener();
        listener.Accepted += listener_Accepted;

        //Create the timer and register the event.
        tmrOverallProg = new Timer();
        tmrOverallProg.Interval = 1000;
        tmrOverallProg.Tick += tmrOverallProg_Tick;

        //Set our default output folder.
        outputFolder = "Transfers";

        //If it does not exist, create it.
        if (!Directory.Exists(outputFolder))
        {
            Directory.CreateDirectory(outputFolder);
        }

        btnConnect.Click += new EventHandler(btnConnect_Click);
        btnStartServer.Click += new EventHandler(btnStartServer_Click);
        btnStopServer.Click += new EventHandler(btnStopServer_Click);
        btnSendFile.Click += new EventHandler(btnSendFile_Click);
        btnPauseTransfer.Click += new EventHandler(btnPauseTransfer_Click);
        btnStopTransfer.Click += new EventHandler(btnStopTransfer_Click);
        btnOpenDir.Click += new EventHandler(btnOpenDir_Click);
        btnClearComplete.Click += new EventHandler(btnClearComplete_Click);

        btnStopServer.Enabled = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        //Deregister all the events from the client if it is connected.
        deregisterEvents();
        base.OnFormClosing(e);
    }

    void tmrOverallProg_Tick(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        //Get and display the overall progress.
        progressOverall.Value = transferClient.GetOverallProgress();
    }

    void listener_Accepted(object sender, SocketAcceptedEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new SocketAcceptedHandler(listener_Accepted), sender, e);
            return;
        }

        //Stop the listener
        listener.Stop();

        //Create the transfer client based on our newly connected socket.
        transferClient = new TransferClient(e.Accepted);
        //Set our output folder.
        transferClient.OutputFolder = outputFolder;
        //Register the events.
        registerEvents();
        //Run the client
        transferClient.Run();
        //Start the progress timer
        tmrOverallProg.Start();
        //And set the new connection state.
        setConnectionStatus(transferClient.EndPoint.Address.ToString());
    }

    private void btnConnect_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
        {
            //Create our new transfer client.
            //And attempt to connect
            transferClient = new TransferClient();
            transferClient.Connect(txtCntHost.Text.Trim(), int.Parse(txtCntPort.Text.Trim()), connectCallback);
            Enabled = false;
        }
        else
        {
            //This means we're trying to disconnect.
            transferClient.Close();
            transferClient = null;
        }
    }

    private void connectCallback(object sender, string error)
    {
        if (InvokeRequired)
        {
            Invoke(new ConnectCallback(connectCallback), sender, error);
            return;
        }
        //Set the form to enabled.
        Enabled = true;
        //If the error is not equal to null, something went wrong.
        if (error != null)
        {
            transferClient.Close();
            transferClient = null;
            MessageBox.Show(error, "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        //Register the events
        registerEvents();
        //Set the output folder
        transferClient.OutputFolder = outputFolder;
        //Run the client
        transferClient.Run();
        //Set the connection status
        setConnectionStatus(transferClient.EndPoint.Address.ToString());
        //Start the progress timer.
        tmrOverallProg.Start();
        //Set our connect button text to "Disconnect"
        btnConnect.Text = "Disconnect";
    }

    private void registerEvents()
    {
        transferClient.Complete += transferClient_Complete;
        transferClient.Disconnected += transferClient_Disconnected;
        transferClient.ProgressChanged += transferClient_ProgressChanged;
        transferClient.Queued += transferClient_Queued;
        transferClient.Stopped += transferClient_Stopped;
    }

    void transferClient_Stopped(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            Invoke(new TransferEventHandler(transferClient_Stopped), sender, queue);
            return;
        }
        //Remove the stopped transfer from view.
        lstTransfers.Items[queue.ID.ToString()].Remove();
    }

    void transferClient_Queued(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            Invoke(new TransferEventHandler(transferClient_Queued), sender, queue);
            return;
        }

        //Create the LVI for the new transfer.
        ListViewItem i = new ListViewItem();
        i.Text = queue.ID.ToString();
        i.SubItems.Add(queue.Filename);
        //If the type equals download, it will use the string of "Download", if not, it'll use "Upload"
        i.SubItems.Add(queue.Type == QueueType.Download ? "Download" : "Upload");
        i.SubItems.Add("0%");
        i.Tag = queue; //Set the tag to queue so we can grab is easily.
        i.Name = queue.ID.ToString(); //Set the name of the item to the ID of our transfer for easy access.
        lstTransfers.Items.Add(i); //Add the item
        i.EnsureVisible();
        
        //If the type is download, let the uploader know we're ready.
        if (queue.Type == QueueType.Download)
        {
            transferClient.StartTransfer(queue);
        }
    }

    void transferClient_ProgressChanged(object sender, TransferQueue queue)
    {
        if (InvokeRequired)
        {
            Invoke(new TransferEventHandler(transferClient_ProgressChanged), sender, queue);
            return;
        }

        //Set the progress cell to our current progress.
        lstTransfers.Items[queue.ID.ToString()].SubItems[3].Text = queue.Progress + "%";
    }

    void transferClient_Disconnected(object sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new EventHandler(transferClient_Disconnected), sender, e);
            return;
        }

        //Deregister the transfer client events
        deregisterEvents();

        //Close every transfer
        foreach (ListViewItem item in lstTransfers.Items)
        {
            TransferQueue queue = (TransferQueue)item.Tag;
            queue.Close();
        }
        //Clear the listview
        lstTransfers.Items.Clear();
        progressOverall.Value = 0;

        //Set the client to null
        transferClient = null;

        //Set the connection status to nothing
        setConnectionStatus("-");

        //If the server is still running, wait for another connection
        if (serverRunning)
        {
            listener.Start(int.Parse(txtServerPort.Text.Trim()));
            setConnectionStatus("Waiting...");
        }
        else //If we connected then disconnected, set the text back to connect.
        {
            btnConnect.Text = "Connect";
        }
    }

    void transferClient_Complete(object sender, TransferQueue queue)
    {
        //This just plays a little sound to let us know a transfer completed.
        System.Media.SystemSounds.Asterisk.Play();
    }

    private void deregisterEvents()
    {
        if (transferClient == null)
            return;
        transferClient.Complete -= transferClient_Complete;
        transferClient.Disconnected -= transferClient_Disconnected;
        transferClient.ProgressChanged -= transferClient_ProgressChanged;
        transferClient.Queued -= transferClient_Queued;
        transferClient.Stopped -= transferClient_Stopped;
    }

    private void setConnectionStatus(string connectedTo)
    {
        lblConnected.Text = "Connection: " + connectedTo;
    }

    private void btnStartServer_Click(object sender, EventArgs e)
    {
        //We disabled the button, but lets just do a quick check
        if (serverRunning)
            return;
        serverRunning = true;
        try
        {
            //Try to listen on the desired port
            listener.Start(int.Parse(txtServerPort.Text.Trim()));
            //Set the connection status to waiting
            setConnectionStatus("Waiting...");
            //Enable/Disable the server buttons.
            btnStartServer.Enabled = false;
            btnStopServer.Enabled = true;
        }
        catch
        {
            MessageBox.Show("Unable to listen on port " + txtServerPort.Text, "", MessageBoxButtons.OK, MessageBoxIcon.Error);

        }
    }

    private void btnStopServer_Click(object sender, EventArgs e)
    {
        if (!serverRunning)
            return;
        //Close the client if its active.
        if (transferClient != null)
        {
            transferClient.Close();
            //INSERT
            transferClient = null;
            //
        }
        //Stop the listener
        listener.Stop();
        //Stop the timer
        tmrOverallProg.Stop();
        //Reset the connection statis
        setConnectionStatus("-");
        //Set our variables and enable/disable the buttons.
        serverRunning = false;
        btnStartServer.Enabled = true;
        btnStopServer.Enabled = false;
    }

    private void btnClearComplete_Click(object sender, EventArgs e)
    {
        //Loop and clear all complete or inactive transfers
        foreach (ListViewItem i in lstTransfers.Items)
        {
            TransferQueue queue = (TransferQueue)i.Tag;

            if (queue.Progress == 100 || !queue.Running)
            {
                i.Remove();
            }
        }
    }

    private void btnOpenDir_Click(object sender, EventArgs e)
    {
        //Get a user defined save directory
        using (FolderBrowserDialog fb = new FolderBrowserDialog())
        {
            if (fb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                outputFolder = fb.SelectedPath;

                if (transferClient != null)
                {
                    transferClient.OutputFolder = outputFolder;
                }

                txtSaveDir.Text = outputFolder;
            }
        }
    }

    private void btnSendFile_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        //Get the user desired files to send
        using (OpenFileDialog o = new OpenFileDialog())
        {
            o.Filter = "All Files (*.*)|*.*";
            o.Multiselect = true;

            if (o.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                foreach (string file in o.FileNames)
                {
                    transferClient.QueueTransfer(file);
                }
            }
        }
    }

    private void btnPauseTransfer_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;
        //Loop and pause/resume all selected downloads.
        foreach (ListViewItem i in lstTransfers.SelectedItems)
        {
            TransferQueue queue = (TransferQueue)i.Tag;
            queue.Client.PauseTransfer(queue);
        }
    }

    private void btnStopTransfer_Click(object sender, EventArgs e)
    {
        if (transferClient == null)
            return;

        //Loop and stop all selected downloads.
        foreach (ListViewItem i in lstTransfers.SelectedItems)
        {
            TransferQueue queue = (TransferQueue)i.Tag;
            queue.Client.StopTransfer(queue);
            i.Remove();
        }

        progressOverall.Value = 0;
    }
}