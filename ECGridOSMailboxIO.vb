'The following ECGridOS calls are used:
'   ParcelUpload()
'   ParcelUploadEx()
'   ParcelDownload()
'   ParcelDownloadConfirm()
'   ParcelInBox()

'The following ecgrid:analystics calls are used:
'   TransactionConfirmByInterchangeId()

'2016-10-12 - Added support for Target confirmations

Imports System.Web.Services.Protocols
Imports System.Xml
Imports System.IO
Imports System.IO.Compression
Imports System.Net.Mail

Module Module1
    'ECGRIDOS: Set up an Object for the Web Service;
    ' this requires a Web Reference to https://ecgridos.net/v3.2/prod/ecgridos.asmx
    Public ecgridos As New net.ecgridos.ECGridOSAPIv3
    'ECGRIDOS: Have a place to store the APIKey
    Public APIKey As String

    Private Sub Syntax()
        Console.WriteLine()
        Console.WriteLine("ECGridOSMailboxIO <APIKey> <LocalDirectory|LocalFile> <-upload|-download> [-e:ErrorContact] [-target]")
        Console.WriteLine("   WITH TARGET ANALYTICS - sends analytic information for Target Corporation.")
        Console.WriteLine("   APIKey - must be previously registered on ECGridOS")
        Console.WriteLine("   LocalDirectory - the local directory - if upload, all files in the directory")
        Console.WriteLine("                    will be sent.")
        Console.WriteLine("                    Use ""..."" on the command line to allow spaces in the")
        Console.WriteLine("                    LocalDirectory")
        Console.WriteLine("   LocalFile      - a local file for upload only")
        Console.WriteLine("                    Use ""..."" on the command line to allow spaces in the")
        Console.WriteLine("                    LocalFile")
        Console.WriteLine("   -upload[:ECGridIDFrom,ECGridIDTo]-download - direction of file transfer")

        Console.WriteLine("   -e:ErrorContact - substitute a friendly error message with a contact message")
        Console.WriteLine("                     e.g. ""-e:test@example.com""")
        Console.WriteLine("                     Sends to Port 587 TLS on our mail server.")

    End Sub
    Private ErrorContact As String = ""
    Private retVal As Integer = 0

    Private targetAnalytics As Boolean = True

    Function Main() As Integer

        Console.WriteLine("{0} {1:g}", My.Application.Info.ProductName, Now)
        Console.WriteLine("{0} {1}", My.Application.Info.CompanyName, My.Application.Info.Copyright)
        Console.WriteLine("")

        If My.Application.CommandLineArgs.Count < 3 Or My.Application.CommandLineArgs.Count > 5 Then
            Syntax()
            retVal = 1
        Else
            APIKey = My.Application.CommandLineArgs(0)
            Dim FileDir As String = My.Application.CommandLineArgs(1)
            'Dim Upload As Boolean = My.Application.CommandLineArgs(2).ToLower.StartsWith("-upload")
            Dim Upload As Boolean = False
            Dim ECGridIDFrom As Integer = 0
            Dim ECGridIDTo As Integer = 0

            For i = 2 To My.Application.CommandLineArgs.Count - 1
                Dim arg As String = My.Application.CommandLineArgs(i)
                arg = arg.Replace("–", "-") 'replacing em-dash
                Select Case True
                    Case arg.ToLower.StartsWith("-upload")
                        Upload = True
                        If arg.Contains(":") Then
                            If arg.Contains(",") Then
                                Dim ids = arg.Split(":")(1)
                                ECGridIDFrom = ids.Split(",")(0)
                                ECGridIDTo = ids.Split(",")(1)
                            Else 'This is a fix for old code format - allows old : separator
                                ECGridIDFrom = CInt(arg.Split(":")(1))
                                ECGridIDTo = CInt(arg.Split(":")(2))
                            End If
                        End If
                    Case arg.ToLower.StartsWith("-e:")
                        ErrorContact = arg.Substring(3).Trim()
                    Case arg.ToLower = "-target"
                        targetAnalytics = True
                End Select

            Next


            Dim err As String = ""

            Try
                Console.WriteLine("ECGridOS {0}", ecgridos.Version())

                If Upload Then
                    ECGridOSUpload(FileDir, ECGridIDFrom, ECGridIDTo)
                Else
                    ECGridOSDownload(FileDir)
                End If

            Catch ex As SoapException
                'There is good data in the InnerXML
                'which can be parsed and used to processes specific exceptions
                err = ShowSoapError(ex)

            Catch ex As Exception
                If retVal = 0 Then retVal = 2
                err = "ERROR: " & ex.Message & vbCrLf & vbCrLf & ex.StackTrace
                Console.WriteLine(ex.Message)

            End Try

            If err.Length > 0 Then sendError(ErrorContact, err)

        End If

        ecgridos = Nothing

        Return retVal

    End Function

    Private Sub ECGridOSUpload(ByVal FileDir As String, _
                               ByVal ECGridIDFrom As Integer, _
                               ByVal ECGridIDTo As Integer)
        Dim fs As IO.FileStream
        Dim files As System.Collections.ObjectModel.ReadOnlyCollection(Of String)
        Dim buffer() As Byte
        Dim bytes As Long
        Dim ParcelID As Integer

        Try
            'Let's send all files in the specified directory
            files = My.Computer.FileSystem.GetFiles(FileDir, _
                                                    FileIO.SearchOption.SearchTopLevelOnly, _
                                                    "*.*")
        Catch
            If My.Computer.FileSystem.FileExists(FileDir) Then
                files = My.Computer.FileSystem.GetFiles(Path.GetDirectoryName(FileDir), FileIO.SearchOption.SearchTopLevelOnly, Path.GetFileName(FileDir))
            Else
                retVal = 2
                Throw New System.ApplicationException("File/Path not found")
            End If

        End Try

        For Each FileName As String In files
            Console.WriteLine("Uploading: " & FileName & "...")

            'Load the entire file into a string buffer
            bytes = FileLen(FileName)
            fs = New IO.FileStream(FileName, IO.FileMode.Open, IO.FileAccess.Read)
            ReDim buffer(bytes - 1)
            fs.Read(buffer, 0, bytes)
            fs.Close()

            ecgridos.Timeout = My.Settings.UploadTimeoutSeconds * 1000

            If ECGridIDFrom > 0 Then
                'ECGRIDOS: ParcelUploadEx() posts a file to ECGrid wrapped with an envelope with specified From and To IDs.
                '          Note that this call is deprecated and replaced by ParcelUploadDirected()
                ' The ParcelID can be used as a handle later to get more information
                ' about the specific files as it transits the system.
                ecgridosconsole("WhoAmI(""{0}"")", APIKey)
                Dim whoami As net.ecgridos.SessionInfo = ecgridos.WhoAmI(APIKey)

                ecgridosconsole("ParcelUploadDirected(""{0}"",{1},{2},""{3}"",{4},{5},{6},{7})", APIKey, whoami.NetworkID, whoami.MailboxID, Path.GetFileName(FileName), bytes, "byte[]", ECGridIDFrom, ECGridIDTo)

                ParcelID = ecgridos.ParcelUploadDirected(APIKey, whoami.NetworkID, whoami.MailboxID, Path.GetFileName(FileName), bytes, buffer, ECGridIDFrom, ECGridIDTo)
            Else
                'ECGRIDOS: ParcelUpload() posts a file to ECGrid
                ' The ParcelID can be used as a handle later to get more information
                ' about the specific files as it transits the system.
                ecgridosconsole("ParcelUpload(""{0}"",""{1}"",{2},{3})", APIKey, Path.GetFileName(FileName), bytes, "byte[]")
                ParcelID = ecgridos.ParcelUpload(APIKey, Path.GetFileName(FileName), bytes, buffer)
            End If


            Console.WriteLine("ParcelID = {0}", ParcelID)

            'Delete the file after a successful upload.
            'Note that the Try/Catch from the calling routine will handle any errors
            ' and not delete the file if it doesn't upload
            My.Computer.FileSystem.DeleteFile(FileName)
        Next
    End Sub

    Private Sub ECGridOSDownload(ByVal FileDir As String)
        'An array of ParcelIDInfo objects stores the results of a call to
        ' the ParcelInBox() API.
        Dim Parcels As net.ecgridos.ParcelIDInfoCollection
        Dim Parcel As net.ecgridos.ParcelIDInfo
        Dim ParcelID As Integer
        'The FileInfo object holds all the info and payload of the downloaded file
        Dim FileInfo As net.ecgridos.FileInfo
        Dim fs As IO.FileStream

        If Right(FileDir, 1) <> "\" Then FileDir = FileDir & "\"

        'ECGRIDOS: ParcelInBox() is used to lists all pending files in the current
        ' Mailbox("In Box"). A collection/array of ParcelIDInfo objects is returned.

        ecgridosconsole("ParcelInBox(""{0}"")", APIKey)
        Parcels = ecgridos.ParcelInBox(APIKey)

        For Each Parcel In Parcels.ParcelIDInfoList

            Console.WriteLine("Downloading: {0}  ParcelID: {1}  Bytes: {2}", Parcel.FileName, Parcel.ParcelID, Parcel.ParcelBytes)

            ParcelID = Parcel.ParcelID

            'ECGRIDOS: ParcelDownload() is used to download the File info & content;
            ' it returns the FileInfo object

            ecgridosconsole("ParcelDownload(""{0}"",{1})", APIKey, ParcelID)
            FileInfo = ecgridos.ParcelDownload(APIKey, ParcelID)
            Dim err As String = ""
            Try
                'Save the payload to a file as the same name as in the FileInfo object
                ' in the commandline specified directory.
                fs = New IO.FileStream(FileDir & FileInfo.FileName, _
                                       IO.FileMode.Create, _
                                       IO.FileAccess.Write)
                fs.Write(FileInfo.Content, 0, FileInfo.Bytes)
                fs.Close()

                'ECGRIDOS: ParcelDownloadConfirm is used to tell ECGrid to mark the file
                ' as downloaded and remove it from the InBox. A copy of the file remains
                ' in the Archive.
                ecgridosconsole("ParcelDownloadConfirm(""{0}"",{1})", APIKey, ParcelID)
                ecgridos.ParcelDownloadConfirm(APIKey, ParcelID)
                Console.WriteLine("complete.")
                If targetAnalytics Then targetConfirm(Parcel)

            Catch ex As SoapException
                'There is good data in the InnerXML
                'which can be parsed and used to processes specific exceptions
                err = ShowSoapError(ex)
                retVal = 5

            Catch ex As Exception
                err = "ERROR: " & ex.ToString
                Console.WriteLine(err)
                retVal = 2
            End Try
            If err.Length > 0 Then
                sendError(ErrorContact, err)
                ecgridosconsole("ParcelDownloadReset(""{0}"",{1})", APIKey, ParcelID)
                Try
                    ecgridos.ParcelDownloadReset(APIKey, ParcelID)
                    Console.WriteLine("error: download reset.")
                Catch ex1 As SoapException
                End Try

            End If
        Next
    End Sub

    Private Sub targetConfirm(ByVal parcel As net.ecgridos.ParcelIDInfo)
        For Each inter In parcel.Interchanges
            'See if it is from Target - NetworkID:=506 and if it is an 850 or 860
            Try
                If inter.NetworkIDFrom = 506 AndAlso (("+" & inter.DocumentType).Contains("+850:") OrElse ("+" & inter.DocumentType).Contains("+860:")) Then
                    Dim analytics As New com.ecgrid.analytics.ecgridanalyticsv1
                    Dim response As com.ecgrid.analytics.TransStatus = analytics.TransactionConfirmByInterchangeId(APIKey, inter.InterchangeID, com.ecgrid.analytics.ConfirmationEvent.MailboxPickedUp, "")
                    analyticsconsole("TransactionConfirmByInterchangeID(""{0}"",{1},{2},""{3}"")", APIKey, inter.InterchangeID, com.ecgrid.analytics.ConfirmationEvent.MailboxPickedUp, "")
                    Console.WriteLine("Target: {0}", response)
                End If
            Catch ex As Exception

            End Try
        Next
    End Sub

    Private Sub ecgridosconsole(ByVal s As String, ByVal ParamArray a() As Object)
        Dim c As ConsoleColor = Console.ForegroundColor
        Dim cmd As String = String.Format(s, a)
        Console.Write("ecgridos.")
        Console.ForegroundColor = ConsoleColor.White
        Console.Write(Left(cmd, cmd.IndexOf("(")))
        Console.ForegroundColor = c
        Console.WriteLine(Mid(cmd, cmd.IndexOf("(") + 1))
    End Sub

    Private Sub analyticsconsole(ByVal s As String, ByVal ParamArray a() As Object)
        Dim c As ConsoleColor = Console.ForegroundColor
        Dim cmd As String = String.Format(s, a)
        Console.Write("analytics.")
        Console.ForegroundColor = ConsoleColor.White
        Console.Write(Left(cmd, cmd.IndexOf("(")))
        Console.ForegroundColor = c
        Console.WriteLine(Mid(cmd, cmd.IndexOf("(") + 1))
    End Sub

    Private Function ShowSoapError(ByVal ex As SoapException) As String

        Dim doc As New XmlDocument
        Dim Node As XmlNode

        Dim msg As String = ""

        '*** not implemented at this time ***
        'If Contact.Length > 0 Then Console.WriteLine("ERROR: Unable to connect to ECGridOS server at this time, will retry. Please report to {0} if this problem persists more than 15 minutes.", Contact)

        doc.LoadXml(ex.Detail.OuterXml)
        Node = doc.DocumentElement.SelectSingleNode("ErrorInfo")
        If Node Is Nothing Then
            msg &= "ERROR: " & ex.Message.ToString & vbCrLf & vbCrLf & ex.StackTrace
        Else
            msg &= [String].Format("SOAP Exception: ({0}) {1}" & vbCrLf, Node.SelectSingleNode("ErrorCode").InnerText, Node.SelectSingleNode("ErrorString").InnerText)
            msg &= [String].Format("    Error Item: {0}" & vbCrLf, Node.SelectSingleNode("ErrorItem").InnerText)
            msg &= [String].Format("           Msg: {0}" & vbCrLf, Node.SelectSingleNode("ErrorMessage").InnerText)
        End If
        Console.WriteLine(msg)
        Return msg

    End Function

    Private Sub sendError(ByVal contact As String, ByVal errMsg As String)
        If contact.Length = 0 Then Return
        Try

            Dim config As String = "Config: " & vbCrLf & vbCrLf
            For i = 0 To My.Application.CommandLineArgs.Count - 1

                config &= String.Format("arg({0}): {1}" & vbCrLf, i, My.Application.CommandLineArgs(i))

            Next

            With My.Application.Info
                errMsg &= vbCrLf
                errMsg &= vbCrLf
                errMsg &= config
                errMsg &= vbCrLf
                errMsg &= vbCrLf
                errMsg &= String.Format("{0} v{1}" & vbCrLf, .AssemblyName, .Version)
                errMsg &= String.Format("{0}, {1}" & vbCrLf, .Copyright, .CompanyName)
            End With


            Dim msg As New MailMessage
            msg.Subject = "ECGridOSMailboxIO: Error"
            msg.From = New MailAddress("support@ecgrid.com")
            msg.To.Add(contact)
            msg.Body = errMsg

            Dim accept As New clsSSL
            System.Net.ServicePointManager.ServerCertificateValidationCallback = AddressOf accept.AcceptAllCertifications

            Dim smtp As New SmtpClient("smtp-ssl.ld.com", 587)
            smtp.EnableSsl = True
            smtp.DeliveryMethod = SmtpDeliveryMethod.Network
            smtp.UseDefaultCredentials = False
            smtp.Credentials = New System.Net.NetworkCredential("ecgridos", "Io4)uae9hnDz~vcz")

            smtp.Send(msg)
            Console.WriteLine("Error message sent to " & contact)

        Catch ex As Exception
            Console.WriteLine(ex.Message)

        End Try
    End Sub

    Public Class clsSSL
        Public Function AcceptAllCertifications(ByVal sender As Object, ByVal certification As System.Security.Cryptography.X509Certificates.X509Certificate, ByVal chain As System.Security.Cryptography.X509Certificates.X509Chain, ByVal sslPolicyErrors As System.Net.Security.SslPolicyErrors) As Boolean
            Return True
        End Function
    End Class
End Module
