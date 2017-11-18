﻿Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.DirectoryServices
Imports System.DirectoryServices.Protocols
Imports System.Net
Imports System.Threading
Imports System.Threading.Tasks
Imports CredentialManagement
Imports HandlebarsDotNet
Imports IRegisty

Public Class clsDomain
    Implements INotifyPropertyChanged

    Public Event PropertyChanged(sender As Object, e As System.ComponentModel.PropertyChangedEventArgs) Implements System.ComponentModel.INotifyPropertyChanged.PropertyChanged

    Private _name As String
    Private _username As String
    Private _password As String
    Private _server As String

    Private _connection As LdapConnection

    Private _defaultnamingcontext As String
    Private _configurationnamingcontext As String
    Private _schemanamingcontext As String
    Private _searchroot As String

    Private _properties As New ObservableCollection(Of clsDomainProperty)
    Private _maxpwdage As Integer
    Private _suffixes As New ObservableCollection(Of String)

    Private _usernamepattern As String = ""
    Private _usernamepatterntemplates As Func(Of Object, String)() = Nothing

    Private _computerpattern As String = ""
    Private _computerpatterntemplates As Func(Of Object, String)() = Nothing

    Private _telephonenumberpattern As New ObservableCollection(Of clsTelephoneNumberPattern)

    Private _defaultpassword As String = ""

    Private _defaultgroups As New ObservableCollection(Of clsDirectoryObject)
    Private _defaultgroupsdn As ObservableCollection(Of String)

    Private _exchangeservers As New ObservableCollection(Of String)
    Private _useexchange As Boolean
    Private _exchangeserver As String

    Private _mailboxpattern As String = ""

    Private _issearchable As Boolean = True
    Private _validated As Boolean

    Protected Overridable Sub OnPropertyChanged(e As PropertyChangedEventArgs)
        RaiseEvent PropertyChanged(Me, e)
    End Sub

    Private Sub NotifyPropertyChanged(propertyName As String)
        OnPropertyChanged(New PropertyChangedEventArgs(propertyName))
    End Sub

    Sub New()

    End Sub

    <RegistrySerializerAfterSerialize(True)>
    Public Sub AfterSerialize()
        Dim cred As New Credential("", "", My.Application.Info.AssemblyName & ": " & Name, CredentialType.Generic)
        cred.PersistanceType = PersistanceType.Enterprise
        cred.Username = _username
        cred.Password = _password
        cred.Save()
    End Sub

    <RegistrySerializerAfterDeserialize(True)>
    Public Sub AfterDeserialize()
        Dim cred As New Credential("", "", My.Application.Info.AssemblyName & ": " & Name, CredentialType.Generic)
        cred.PersistanceType = PersistanceType.Enterprise
        cred.Load()
        Username = cred.Username
        Password = cred.Password

        Validated = False
        If String.IsNullOrEmpty(Name) Or String.IsNullOrEmpty(Username) Or String.IsNullOrEmpty(Password) Then Exit Sub

        Dim endpoint As String = If(String.IsNullOrEmpty(Server), Name, Server)

        If Connection IsNot Nothing Then Connection.Dispose()

        Dim ldapdi As New LdapDirectoryIdentifier(endpoint)
        Dim ldapnc As New NetworkCredential(Username, Password)
        Dim ldapconnection As New LdapConnection(ldapdi, ldapnc)
        ldapconnection.SessionOptions.TcpKeepAlive = True
        ldapconnection.SessionOptions.AutoReconnect = True
        ldapconnection.AutoBind = True
        Try
            ldapconnection.Bind()
            Connection = ldapconnection
            Validated = True
        Catch ex As Exception
            Connection = Nothing
            Exit Sub
        End Try

        If String.IsNullOrEmpty(DefaultNamingContext) Then Exit Sub
        If String.IsNullOrEmpty(ConfigurationNamingContext) Then Exit Sub
        If String.IsNullOrEmpty(SearchRoot) Then Exit Sub
        If String.IsNullOrEmpty(SchemaNamingContext) Then Exit Sub

        Try
            If DefaultGroupsDN.Count > 0 Then DefaultGroups = New ObservableCollection(Of clsDirectoryObject)(DefaultGroupsDN.Select(Function(x As String) New clsDirectoryObject(x, Me)).ToList)
        Catch ex As Exception

        End Try

        Validated = True
    End Sub

    Public Async Function ConnectAsync() As Task(Of Boolean)
        Validated = False
        If String.IsNullOrEmpty(Name) Or String.IsNullOrEmpty(Username) Or String.IsNullOrEmpty(Password) Then Return False

        Dim endpoint As String = If(String.IsNullOrEmpty(Server), Name, Server)

        If Connection IsNot Nothing Then Connection.Dispose()

        Dim ldapdi As New LdapDirectoryIdentifier(endpoint)
        Dim ldapnc As New NetworkCredential(Username, Password)
        Dim ldapconnection As New LdapConnection(ldapdi, ldapnc)
        ldapconnection.SessionOptions.TcpKeepAlive = True
        ldapconnection.SessionOptions.AutoReconnect = True
        ldapconnection.AutoBind = True
        Try
            ldapconnection.Bind()
            Connection = ldapconnection
        Catch ex As Exception
            Connection = Nothing
            Return False
        End Try

        Dim success As Boolean = False

        'defaultNamingContext
        'configurationNamingContext
        'schemaNamingContext
        success = Await Task.Run(
            Function()
                Try
                    Dim searchRequest = New SearchRequest(Nothing, "(objectClass=*)", Protocols.SearchScope.Base, {"defaultNamingContext", "configurationNamingContext", "schemaNamingContext"})
                    Dim response As SearchResponse = Connection.SendRequest(searchRequest)
                    DefaultNamingContext = response.Entries(0).Attributes("defaultNamingContext")(0)
                    ConfigurationNamingContext = response.Entries(0).Attributes("configurationNamingContext")(0)
                    SchemaNamingContext = response.Entries(0).Attributes("schemaNamingContext")(0)
                Catch
                    DefaultNamingContext = Nothing
                    ConfigurationNamingContext = Nothing
                    SchemaNamingContext = Nothing
                    Return False
                End Try
                Return True
            End Function)
        If Not success Then Return False


        'properties
        Properties = Await Task.Run(
            Function()
                Dim p As New ObservableCollection(Of clsDomainProperty)
                Try
                    Dim searchRequest = New SearchRequest(DefaultNamingContext, "(objectClass=*)", Protocols.SearchScope.Base, {"lockoutThreshold", "lockoutDuration", "lockOutObservationWindow", "maxPwdAge", "minPwdAge", "minPwdLength", "pwdProperties", "pwdHistoryLength"})
                    Dim response As SearchResponse = Connection.SendRequest(searchRequest)

                    MaxPwdAge = -TimeSpan.FromTicks(Long.Parse(response.Entries(0).Attributes("maxPwdAge")(0))).Days

                    p.Clear()
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropLockoutThreshold, String.Format(My.Resources.clsDomain_msg_PropLockoutThresholdFormat, response.Entries(0).Attributes("lockoutThreshold")(0))))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropLockoutDuration, String.Format(My.Resources.clsDomain_msg_PropLockoutDurationFormat, -TimeSpan.FromTicks(Long.Parse(response.Entries(0).Attributes("lockoutDuration")(0))).Minutes)))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropLockoutObservationWindow, String.Format(My.Resources.clsDomain_msg_PropLockoutObservationWindowFormat, -TimeSpan.FromTicks(Long.Parse(response.Entries(0).Attributes("lockOutObservationWindow")(0))).Minutes)))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropMaximumPasswordAge, String.Format(My.Resources.clsDomain_msg_PropMaximumPasswordAgeFormat, -TimeSpan.FromTicks(Long.Parse(response.Entries(0).Attributes("maxPwdAge")(0))).Days)))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropMinimumPasswordAge, String.Format(My.Resources.clsDomain_msg_PropMinimumPasswordAgeFormat, -TimeSpan.FromTicks(Long.Parse(response.Entries(0).Attributes("minPwdAge")(0))).Days)))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropMinimumPasswordLenght, String.Format(My.Resources.clsDomain_msg_PropMinimumPasswordLenghtFormat, response.Entries(0).Attributes("minPwdLength")(0))))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropPasswordComplexityRequirements, String.Format(My.Resources.clsDomain_msg_PropPasswordComplexityRequirementsFormat, CBool(response.Entries(0).Attributes("pwdProperties")(0)))))
                    p.Add(New clsDomainProperty(My.Resources.clsDomain_msg_PropPasswordHistory, String.Format(My.Resources.clsDomain_msg_PropPasswordHistoryFormat, response.Entries(0).Attributes("pwdHistoryLength")(0))))
                Catch ex As Exception
                    p.Clear()
                End Try
                Return p
            End Function)

        'domain suffixes
        Suffixes = Await Task.Run(
            Function()
                Dim s As New ObservableCollection(Of String)
                Try
                    Dim searchRequest = New SearchRequest(ConfigurationNamingContext, "(&(objectClass=crossRef)(systemFlags=3))", Protocols.SearchScope.Subtree, {"dnsRoot"})
                    Dim response As SearchResponse = Connection.SendRequest(searchRequest)

                    For Each suffix As SearchResultEntry In response.Entries
                        s.Add(LCase(suffix.Attributes("dnsRoot")(0)))
                    Next suffix
                Catch ex As Exception
                    s.Clear()
                End Try
                Return s
            End Function)


        'search root
        If String.IsNullOrEmpty(SearchRoot) AndAlso Not String.IsNullOrEmpty(DefaultNamingContext) Then SearchRoot = DefaultNamingContext

        'exchange servers
        ExchangeServers = Await Task.Run(
            Function()
                Dim e As New ObservableCollection(Of String)
                Try
                    Dim searchRequest = New SearchRequest(ConfigurationNamingContext, "(objectClass=msExchExchangeServer)", Protocols.SearchScope.Subtree, {"name"})
                    Dim response As SearchResponse = Connection.SendRequest(searchRequest)

                    For Each exch As SearchResultEntry In response.Entries
                        e.Add(LCase(exch.Attributes("name")(0)))
                    Next exch
                Catch ex As Exception
                    e.Clear()
                End Try
                Return e
            End Function)

        If Not String.IsNullOrEmpty(ExchangeServer) AndAlso Not ExchangeServers.Contains(ExchangeServer) Then
            UseExchange = False
            ExchangeServer = Nothing
        End If

        Validated = True

        Return True
    End Function


    Public Property Name() As String
        Get
            Return _name
        End Get
        Set(value As String)
            _name = value
            NotifyPropertyChanged("Name")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public Property Username() As String
        Get
            Return _username
        End Get
        Set(value As String)
            _username = value
            NotifyPropertyChanged("Username")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public Property Password() As String
        Get
            Return _password
        End Get
        Set(value As String)
            _password = value
            NotifyPropertyChanged("Password")
        End Set
    End Property

    <RegistrySerializerAlias("Server")>
    Public Property Server() As String
        Get
            Return _server
        End Get
        Set(value As String)
            _server = value
            NotifyPropertyChanged("Server")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public Property Connection() As LdapConnection
        Get
            Return _connection
        End Get
        Set(value As LdapConnection)
            _connection = value
            NotifyPropertyChanged("Connection")
        End Set
    End Property




    <RegistrySerializerAlias("DefaultNamingContext")>
    Public Property DefaultNamingContext() As String
        Get
            Return _defaultnamingcontext
        End Get
        Set(value As String)
            _defaultnamingcontext = value
            NotifyPropertyChanged("DefaultNamingContextDN")
        End Set
    End Property

    <RegistrySerializerAlias("ConfigurationNamingContext")>
    Public Property ConfigurationNamingContext() As String
        Get
            Return _configurationnamingcontext
        End Get
        Set(value As String)
            _configurationnamingcontext = value
            NotifyPropertyChanged("ConfigurationNamingContextDN")
        End Set
    End Property

    <RegistrySerializerAlias("ConfigurationNamingContext")>
    Public Property SchemaNamingContext() As String
        Get
            Return _schemanamingcontext
        End Get
        Set(value As String)
            _schemanamingcontext = value
            NotifyPropertyChanged("SchemaNamingContextDN")
        End Set
    End Property

    <RegistrySerializerAlias("SearchRoot")>
    Public Property SearchRoot() As String
        Get
            Return _searchroot
        End Get
        Set(value As String)
            _searchroot = value
            NotifyPropertyChanged("SearchRootDN")
        End Set
    End Property




    <RegistrySerializerIgnorable(True)>
    Public Property Properties() As ObservableCollection(Of clsDomainProperty)
        Get
            Return _properties
        End Get
        Set(value As ObservableCollection(Of clsDomainProperty))
            _properties = value
            NotifyPropertyChanged("Properties")
        End Set
    End Property

    Public Property MaxPwdAge() As Integer
        Get
            Return _maxpwdage
        End Get
        Set(value As Integer)
            _maxpwdage = value
            NotifyPropertyChanged("MaxPwdAge")
        End Set
    End Property

    Public Property Suffixes As ObservableCollection(Of String)
        Get
            Return _suffixes
        End Get
        Set(value As ObservableCollection(Of String))
            _suffixes = value
            NotifyPropertyChanged("Suffixes")
        End Set
    End Property

    Public Property UsernamePattern() As String
        Get
            Return _usernamepattern
        End Get
        Set(value As String)
            _usernamepattern = value
            _usernamepatterntemplates = Nothing
            NotifyPropertyChanged("UsernamePattern")
            NotifyPropertyChanged("UsernamePatternTemplates")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public ReadOnly Property UsernamePatternTemplates As Func(Of Object, String)()
        Get
            If _usernamepatterntemplates IsNot Nothing Then Return _usernamepatterntemplates

            Dim patterns() As String = UsernamePattern.Split({",", vbCr, vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Select(Function(x) Trim(x)).ToArray
            _usernamepatterntemplates = patterns.Select(Function(x) Handlebars.Compile(x)).ToArray
            Return _usernamepatterntemplates
        End Get
    End Property

    Public Property ComputerPattern() As String
        Get
            Return _computerpattern
        End Get
        Set(value As String)
            _computerpattern = value
            _computerpatterntemplates = Nothing
            NotifyPropertyChanged("ComputerPattern")
            NotifyPropertyChanged("ComputerPatternTemplates")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public ReadOnly Property ComputerPatternTemplates As Func(Of Object, String)()
        Get
            If _computerpatterntemplates IsNot Nothing Then Return _computerpatterntemplates

            Dim patterns() As String = ComputerPattern.Split({",", vbCr, vbCrLf, vbLf}, StringSplitOptions.RemoveEmptyEntries).Select(Function(x) Trim(x)).ToArray
            _computerpatterntemplates = patterns.Select(Function(x) Handlebars.Compile(x)).ToArray
            Return _computerpatterntemplates
        End Get
    End Property

    Public Property MailboxPattern As String
        Get
            Return _mailboxpattern
        End Get
        Set(value As String)
            _mailboxpattern = value
            NotifyPropertyChanged("MailboxPattern")
        End Set
    End Property

    Public Property TelephoneNumberPattern() As ObservableCollection(Of clsTelephoneNumberPattern)
        Get
            Return _telephonenumberpattern
        End Get
        Set(value As ObservableCollection(Of clsTelephoneNumberPattern))
            _telephonenumberpattern = value
            NotifyPropertyChanged("TelephoneNumberPattern")
        End Set
    End Property

    Public Property DefaultPassword() As String
        Get
            Return _defaultpassword
        End Get
        Set(value As String)
            _defaultpassword = value
            NotifyPropertyChanged("DefaultPassword")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public Property DefaultGroups() As ObservableCollection(Of clsDirectoryObject)
        Get
            Return _defaultgroups
        End Get
        Set(value As ObservableCollection(Of clsDirectoryObject))
            _defaultgroups = value
            NotifyPropertyChanged("DefaultGroups")
        End Set
    End Property

    Public Property DefaultGroupsDN() As ObservableCollection(Of String)
        Get
            Return _defaultgroupsdn
        End Get
        Set(value As ObservableCollection(Of String))
            _defaultgroupsdn = value
            NotifyPropertyChanged("DefaultGroupsDN")
        End Set
    End Property

    Public Property ExchangeServers() As ObservableCollection(Of String)
        Get
            Return _exchangeservers
        End Get
        Set(value As ObservableCollection(Of String))
            _exchangeservers = value
            NotifyPropertyChanged("ExchangeServers")
        End Set
    End Property

    Public Property UseExchange() As Boolean
        Get
            Return _useexchange
        End Get
        Set(value As Boolean)
            _useexchange = value
            NotifyPropertyChanged("UseExchange")
        End Set
    End Property

    Public Property ExchangeServer() As String
        Get
            Return _exchangeserver
        End Get
        Set(value As String)
            _exchangeserver = value
            NotifyPropertyChanged("ExchangeServer")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public Property Validated() As Boolean
        Get
            Return _validated
        End Get
        Set(value As Boolean)
            _validated = value
            NotifyPropertyChanged("Validated")
        End Set
    End Property

    <RegistrySerializerIgnorable(True)>
    Public Property IsSearchable() As Boolean
        Get
            Return _issearchable
        End Get
        Set(value As Boolean)
            _issearchable = value
            NotifyPropertyChanged("IsSearchable")
        End Set
    End Property

End Class
