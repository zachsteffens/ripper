'Imports System.Windows.Interop

'Class MainWindow
'    Protected Overrides Sub OnSourceInitialized(e As EventArgs)

'        MyBase.OnSourceInitialized(e)

'        ' Adds the windows message processing hook And registers USB device add/removal notification.
'        Dim source As HwndSource = HwndSource.FromHwnd(New WindowInteropHelper(Me).Handle)
'        Dim windowHandle As IntPtr = New WindowInteropHelper(Application.Current.MainWindow).Handle
'        If (Not IsNothing(source)) Then

'            windowHandle = source.Handle
'            source.AddHook(HwndHandler);
'            UsbNotification.RegisterUsbDeviceNotification(windowHandle)
'        End If
'    End Sub

'    Private Function HwndHandler(hwnd As IntPtr, msg As Integer, wparam As IntPtr, lparam As IntPtr, ByRef handled As Boolean) As IntPtr

'        If (msg = deviceDetector.MediaInsertedNotification.WmDevicechange) Then
'            Select Case CInt(wparam)
'                Case deviceDetector.MediaInsertedNotification.DbtDeviceremovecomplete
'                    MessageBox.Show("dvd removed")
'                Case deviceDetector.MediaInsertedNotification.DbtDevicearrival
'                    MessageBox.Show("dvd inserted")
'            End Select

'        End If

'        handled = False
'        Return IntPtr.Zero
'    End Function
'End Class


