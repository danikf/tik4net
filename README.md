tik4net
====

Unique complex mikrotik API communication solution.

The tik4net project provides easy to use API to connect and manage mikrotik routers via mikrotik API protocol.
It has two parts:
* basic ADO.NET like API - to perform R/W access to mikrotik in both sync and async code (tik4net.dll).
* O/R mapper like highlevel API with imported mikrotik strong-typed entities.  (tik4net.objects.dll) 

# Licenses
* Apache 2.0.

# Getting started and documentation
See project wiki.
  
# ROADMAP: (for contributors)
Basic ADO.NET api is almost in final state. Work on highlevel O/R mapper part should be done.

* creating highlevel classes for all mikrotik entities
* rewrite collection merge API from previous beta
* add SSL support
* add examples and documentation
* create and contribute to tiktop (see iftop) project 

REMARKS: This project is rewritten version of tik4net (last version was 0.9.7.)