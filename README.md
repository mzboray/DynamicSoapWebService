# DynamicSoapWebService

An example showing how to dynamically call a SOAP based web service from C#.

The general approach is to use the facilities provided by System.Web.Services.dll to inspect the service WSDL, 
generate C# code, and compile it on the fly. Reflection is used to inspect the proxy assembly and invoke 
methods. This supports basic "primitive" types (int, string, byte, etc), simple types (e.g. C# enums, 
Guid, DateTime, etc), and complex argument types (i.e. classes).